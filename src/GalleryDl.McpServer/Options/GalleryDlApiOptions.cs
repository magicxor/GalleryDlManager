using System.ComponentModel.DataAnnotations;

namespace GalleryDl.McpServer.Options;

public sealed class GalleryDlApiOptions
{
    public const string SectionName = "GalleryDlApi";

    [Required, Url]
    public string BaseUrl { get; set; } = "http://gallerydl-webapi:8080";

    [Range(5, 7200)]
    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Upper bound for the download_gallery 'take' argument; may be stricter than the WebApi's own MaxTake.</summary>
    [Range(1, 100)]
    public int MaxTake { get; set; } = 10;

    /// <summary>Absolute directory prefixes the download_gallery tool is allowed to write under.</summary>
    [MinLength(1)]
    public List<string> AllowedPathPrefixes { get; set; } = [];
}
