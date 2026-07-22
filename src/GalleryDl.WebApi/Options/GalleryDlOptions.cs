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
    public int MaxTake { get; set; } = 50;

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

    /// <summary>
    /// Optional variant of <see cref="UrlTemplate"/> with the site-specific "sort by score, highest
    /// first" syntax baked in (e.g. "...&amp;tags={query}+sort:score:desc"). gallery-dl itself cannot
    /// reorder results, so rating sorting only works where the site accepts it in the search query.
    /// When set, it is always used in preference to <see cref="UrlTemplate"/>; null means the
    /// resource cannot sort by rating and its default ordering is used.
    /// </summary>
    public string? RatingSortUrlTemplate { get; set; }

    /// <summary>
    /// Separator this site uses to combine multiple tags. Callers always pass tags as a
    /// space-separated string; the API re-joins them with this separator before building the URL.
    /// Defaults to a single space (the booru / moebooru / shimmie convention); the furry34-family
    /// sites (furry34, rule34vault, yiffverse) need "|".
    /// </summary>
    public string TagSeparator { get; set; } = " ";
}
