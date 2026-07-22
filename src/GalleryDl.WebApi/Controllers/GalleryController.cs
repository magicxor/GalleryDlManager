using System.Net.Http.Headers;
using GalleryDl.WebApi.Options;
using GalleryDl.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace GalleryDl.WebApi.Controllers;

[ApiController]
public sealed class GalleryController(
    GalleryDlRunner runner,
    IOptions<GalleryDlOptions> options,
    FileExtensionContentTypeProvider contentTypeProvider,
    ILogger<GalleryController> logger) : ControllerBase
{
    [HttpGet("/api/resources")]
    public ActionResult<IEnumerable<string>> GetResources() =>
        Ok(options.Value.Resources.Keys.Order());

    [HttpGet("/api/download")]
    public async Task<IActionResult> Download(
        [FromQuery] string resource,
        [FromQuery] string query,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 5,
        CancellationToken ct = default)
    {
        var o = options.Value;

        if (string.IsNullOrWhiteSpace(resource) || !o.Resources.TryGetValue(resource.Trim(), out var resourceOptions))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unknown resource",
                detail: $"Resource '{resource}' is not configured. Available resources: {string.Join(", ", o.Resources.Keys.Order())}.");

        if (string.IsNullOrWhiteSpace(query))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid query",
                detail: "Parameter 'query' must be a non-empty string.");

        if (skip < 0)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid skip",
                detail: "Parameter 'skip' must be >= 0.");

        if (take < 1 || take > o.MaxTake)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid take",
                detail: $"Parameter 'take' must be between 1 and {o.MaxTake}.");

        var url = resourceOptions.UrlTemplate.Replace("{query}", Uri.EscapeDataString(query.Trim()));

        GalleryDlResult result;
        try
        {
            result = await runner.DownloadAsync(url, skip, take, ct);
        }
        catch (GalleryDlTimeoutException e)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "gallery-dl timed out", detail: e.Message);
        }

        try
        {
            if (result.Files.Count == 0)
            {
                if (result.ExitCode != 0)
                    return Problem(statusCode: StatusCodes.Status502BadGateway, title: "gallery-dl failed",
                        detail: $"gallery-dl exited with code {result.ExitCode} and produced no files. Stderr: {result.StdErrExcerpt}");

                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No files matched",
                    detail: $"gallery-dl finished successfully but no files matched resource '{resource}', query '{query}', skip {skip}, take {take}.");
            }

            if (result.ExitCode != 0)
                logger.LogWarning("gallery-dl exited with {ExitCode} but {FileCount} file(s) were downloaded; returning partial result. Stderr: {StdErr}",
                    result.ExitCode, result.Files.Count, result.StdErrExcerpt);

            using var content = new MultipartFormDataContent();
            foreach (var file in result.Files)
            {
                var part = new StreamContent(System.IO.File.OpenRead(file));
                part.Headers.ContentType = new MediaTypeHeaderValue(
                    contentTypeProvider.TryGetContentType(file, out var mediaType) ? mediaType : "application/octet-stream");
                content.Add(part, "files", Path.GetFileName(file));
            }

            Response.ContentType = content.Headers.ContentType!.ToString();
            await content.CopyToAsync(Response.Body, ct);
            return new EmptyResult();
        }
        finally
        {
            try
            {
                Directory.Delete(result.WorkDir, recursive: true);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to clean up work dir {WorkDir}", result.WorkDir);
            }
        }
    }
}
