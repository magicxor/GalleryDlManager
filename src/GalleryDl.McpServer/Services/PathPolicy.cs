using GalleryDl.McpServer.Options;
using Microsoft.Extensions.Options;

namespace GalleryDl.McpServer.Services;

/// <summary>
/// Validates target paths supplied by the agent: they must be absolute, free of '.'/'..'
/// segments, and located under one of the configured allowed prefixes.
/// </summary>
public sealed class PathPolicy(IOptions<GalleryDlApiOptions> options)
{
    /// <summary>Returns an error message, or null when the path is acceptable.</summary>
    public string? Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "Parameter 'path' must be a non-empty absolute directory path.";

        if (!Path.IsPathRooted(path))
            return $"Path '{path}' is not absolute. Provide an absolute directory path, e.g. /downloads/my-dir.";

        if (path.Split('/', '\\').Any(segment => segment is "." or ".."))
            return $"Path '{path}' contains a '.' or '..' segment, which is not allowed.";

        var prefixes = options.Value.AllowedPathPrefixes;
        if (!prefixes.Any(prefix => Matches(path, prefix)))
            return $"Path '{path}' is outside the allowed directories. Allowed prefixes: {string.Join(", ", prefixes)}.";

        return null;
    }

    private static bool Matches(string path, string prefix)
    {
        var p = prefix.TrimEnd('/', '\\');
        if (p.Length == 0)
            return false;

        return path.Equals(p, StringComparison.Ordinal)
               || path.StartsWith(p + '/', StringComparison.Ordinal)
               || path.StartsWith(p + '\\', StringComparison.Ordinal);
    }
}
