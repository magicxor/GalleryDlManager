using System.ComponentModel.DataAnnotations;

namespace GalleryDl.WebApi.Options;

public sealed class GalleryDlOptions
{
    public const string SectionName = "GalleryDl";

    [Required]
    public string ExecutablePath { get; set; } = "gallery-dl";

    /// <summary>Root for per-request work dirs; defaults to {temp}/gallery-dl-work when null.</summary>
    public string? WorkRootPath { get; set; }

    [Range(10, 3600)]
    public int TimeoutSeconds { get; set; } = 40;

    [Range(1, 100)]
    public int MaxTake { get; set; } = 20;

    [MinLength(1)]
    public List<string> AllowedExtensions { get; set; } = [];

    public List<string> BlacklistTags { get; set; } = [];

    /// <summary>Extra CLI arguments appended verbatim before the URL.</summary>
    public List<string> ExtraArgs { get; set; } = [];

    [MinLength(1)]
    public Dictionary<string, ResourceOptions> Resources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ResourceOptions
{
    /// <summary>Gallery URL template with a {query} placeholder, e.g. "https://furry34.com/{query}".</summary>
    [Required]
    public string UrlTemplate { get; set; } = "";
}
