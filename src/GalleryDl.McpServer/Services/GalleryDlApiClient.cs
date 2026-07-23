using System.Net.Http.Json;
using GalleryDl.McpServer.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace GalleryDl.McpServer.Services;

public sealed record DownloadOutcome(bool Success, IReadOnlyList<string> SavedFiles, string? Error);

/// <summary>Typed HttpClient for the GalleryDl.WebApi service.</summary>
public sealed class GalleryDlApiClient(HttpClient http, IOptions<GalleryDlApiOptions> options)
{
    // Server-side policy forwarded to the WebApi as allowUnsafe=true|false. Sourced from
    // appsettings.json (default false / SFW-only); the MCP tools never expose it, so the AI agent
    // cannot influence it.
    private string AllowUnsafeValue => options.Value.AllowUnsafe ? "true" : "false";

    public async Task<string[]> ListResourcesAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<string[]>($"api/resources?allowUnsafe={AllowUnsafeValue}", ct) ?? [];

    /// <summary>
    /// Downloads files via the WebApi and saves them into <paramref name="targetDir"/>.
    /// All-or-nothing: parts are staged in a temp dir first, and if any target file already
    /// exists the whole operation fails without writing anything.
    /// </summary>
    public async Task<DownloadOutcome> DownloadAsync(
        string resource, string query, int skip, int take, string targetDir, CancellationToken ct)
    {
        var uri = $"api/download?resource={Uri.EscapeDataString(resource)}&query={Uri.EscapeDataString(query)}&skip={skip}&take={take}&allowUnsafe={AllowUnsafeValue}";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return new(false, [], $"API returned {(int)response.StatusCode} ({response.StatusCode}): {body}");
        }

        var contentType = response.Content.Headers.ContentType?.ToString();
        if (contentType is null || !contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            return new(false, [], $"Unexpected response content type '{contentType ?? "<none>"}', expected multipart/form-data.");

        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value;
        if (string.IsNullOrEmpty(boundary))
            return new(false, [], "Multipart response is missing its boundary.");

        var stagingDir = Path.Combine(Path.GetTempPath(), "gallerydl-mcp-staging", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        try
        {
            var names = new List<string>();
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            {
                var reader = new MultipartReader(boundary, stream);
                while (await reader.ReadNextSectionAsync(ct) is { } section)
                {
                    string? rawName = null;
                    if (section.ContentDisposition is not null
                        && ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
                    {
                        rawName = disposition.FileName.Value ?? disposition.FileNameStar.Value;
                    }

                    var name = Path.GetFileName(rawName ?? "");
                    if (string.IsNullOrWhiteSpace(name))
                        name = $"file_{names.Count}";
                    if (names.Contains(name))
                        return new(false, [], $"Response contained duplicate file name '{name}'; nothing was written.");
                    names.Add(name);

                    await using var fileStream = File.Create(Path.Combine(stagingDir, name));
                    await section.Body.CopyToAsync(fileStream, ct);
                }
            }

            if (names.Count == 0)
                return new(true, [], null);

            var conflicts = names.Where(n => File.Exists(Path.Combine(targetDir, n))).ToList();
            if (conflicts.Count > 0)
                return new(false, [],
                    $"Refusing to overwrite {conflicts.Count} existing file(s) in '{targetDir}': {string.Join(", ", conflicts)}. Nothing was written.");

            Directory.CreateDirectory(targetDir);
            var saved = new List<string>();
            foreach (var name in names)
            {
                var destination = Path.Combine(targetDir, name);
                File.Move(Path.Combine(stagingDir, name), destination, overwrite: false);
                saved.Add(destination);
            }

            return new(true, saved, null);
        }
        finally
        {
            try
            {
                Directory.Delete(stagingDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup of the staging dir.
            }
        }
    }
}
