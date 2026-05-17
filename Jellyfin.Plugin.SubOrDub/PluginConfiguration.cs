using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubOrDub;

/// <summary>
/// Plugin configuration for the Sub or Dub plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the user's preferred language as an ISO 639-2 code (e.g. "eng", "jpn").
    /// Content originally in this language is excluded from the report.
    /// </summary>
    public string DefaultLanguage { get; set; } = "eng";
}
