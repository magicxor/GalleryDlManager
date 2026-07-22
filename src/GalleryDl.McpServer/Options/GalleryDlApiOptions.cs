using System.ComponentModel.DataAnnotations;

namespace GalleryDl.McpServer.Options;

public sealed class GalleryDlApiOptions
{
    public const string SectionName = "GalleryDlApi";

    [Required, Url]
    public string BaseUrl { get; set; } = "http://gallerydl-webapi:8080";

    [Range(1, 120)]
    public int TimeoutMinutes { get; set; } = 10;

    /// <summary>Absolute directory prefixes the download_gallery tool is allowed to write under.</summary>
    [MinLength(1)]
    public List<string> AllowedPathPrefixes { get; set; } = [];
}
