using System.ComponentModel;
using GalleryDl.McpServer.Options;
using GalleryDl.McpServer.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace GalleryDl.McpServer.Tools;

[McpServerToolType]
internal sealed class GalleryTools(
    GalleryDlApiClient api,
    PathPolicy pathPolicy,
    IOptions<GalleryDlApiOptions> options,
    ILogger<GalleryTools> logger)
{
    [McpServerTool(Name = "download_gallery")]
    [Description("Downloads media files matching a query from a supported gallery resource and saves them into a directory. Results are automatically sorted by rating (highest first) on sites that support it. Existing files are never overwritten. Returns the saved file paths.")]
    public async Task<string> DownloadGallery(
        [Description("Resource id, e.g. 'furry34.com'. Use list_resources to see valid values.")] string resource,
        [Description("Tags to search for, separated by spaces; multi-word tags use underscores. All tags must match (AND). E.g. \"dragon\" or \"cat_ears red_coat female\". The server adapts the separator to each site automatically.")] string query,
        [Description("Absolute directory path to save the files into (must be under an allowed prefix, e.g. /downloads/my-dir).")] string path,
        [Description("Number of leading gallery items to skip. Optional, defaults to 0.")] int skip = 0,
        [Description("Number of files to download. Optional, defaults to 1 (a configured server-side maximum also applies).")] int take = 1,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (skip < 0)
                return "Error: 'skip' must be >= 0.";

            var maxTake = options.Value.MaxTake;
            if (take < 1 || take > maxTake)
                return $"Error: 'take' must be between 1 and {maxTake}.";

            if (pathPolicy.Validate(path) is { } pathError)
                return $"Error: {pathError}";

            var outcome = await api.DownloadAsync(resource, query, skip, take, path, cancellationToken);
            if (!outcome.Success)
                return $"Download failed: {outcome.Error}";

            return outcome.SavedFiles.Count == 0
                ? "Download finished, but the API returned no files."
                : $"Saved {outcome.SavedFiles.Count} file(s):\n{string.Join('\n', outcome.SavedFiles)}";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "download_gallery failed");
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "list_resources")]
    [Description("Lists the gallery resources available for download_gallery.")]
    public async Task<string> ListResources(CancellationToken cancellationToken = default)
    {
        try
        {
            var resources = await api.ListResourcesAsync(cancellationToken);
            return resources.Length == 0 ? "No resources are configured." : string.Join('\n', resources);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "list_resources failed");
            return $"Error: {ex.Message}";
        }
    }
}
