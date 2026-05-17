using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubOrDub;

/// <summary>
/// Sub or Dub plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Sub or Dub";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("b5d7e8f9-1a2c-4b3d-8e9f-0a1b2c3d4e5f");

    /// <inheritdoc />
    public override string Description => "Shows which foreign-language TV shows and movies are available dubbed, subbed, both, or not in your preferred language.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "subordub",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.configurationpage.html",
                EnableInMainMenu = false,
                MenuSection = "server",
                MenuIcon = "language",
                DisplayName = "Sub or Dub"
            }
        ];
    }
}
