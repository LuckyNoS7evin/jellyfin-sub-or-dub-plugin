namespace Jellyfin.Plugin.SubOrDub.Models;

/// <summary>
/// Container for a full scan's results, persisted to disk.
/// </summary>
public class ScanResults
{
    /// <summary>Gets or sets when the last scan was completed.</summary>
    public DateTime LastScanTime { get; set; }

    /// <summary>Gets or sets the ISO 639-2 language code that was scanned.</summary>
    public string ScannedLanguage { get; set; } = string.Empty;

    /// <summary>Gets or sets the list of foreign-language media items found during scan.</summary>
    public List<MediaLanguageInfo> Results { get; set; } = [];
}

/// <summary>
/// Language availability information for a single movie or series.
/// </summary>
public class MediaLanguageInfo
{
    /// <summary>Gets or sets the Jellyfin item ID.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the media type: "Movie" or "Series".</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>Gets or sets the detected original language (ISO 639-2 from primary audio stream).</summary>
    public string OriginalLanguage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the availability status in the user's preferred language.
    /// Values: "Dubbed", "Subbed", "Both", "NotAvailable".
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the Jellyfin library (virtual folder) ID this item belongs to.</summary>
    public Guid LibraryId { get; set; }

    /// <summary>Gets or sets the display name of the library this item belongs to.</summary>
    public string LibraryName { get; set; } = string.Empty;
}
