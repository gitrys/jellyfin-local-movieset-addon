using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LocalMovieSets;

/// <summary>
/// The main plugin class — entry point for Jellyfin's plugin loader.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths (injected).</param>
    /// <param name="xmlSerializer">XML serializer for config persistence (injected).</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the singleton instance of this plugin (set during construction).
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Local Movie Sets";

    /// <inheritdoc />
    /// <remarks>
    /// This GUID uniquely identifies this plugin. Do not change it after release
    /// or Jellyfin will treat it as a different plugin.
    /// </remarks>
    public override Guid Id => Guid.Parse("d3e4f5a6-b7c8-9012-def0-123456789abc");

    /// <inheritdoc />
    public override string Description =>
        "Creates movie collections (box sets) from local NFO metadata produced by tinyMediaManager. " +
        "No external API calls — everything is read from your local files.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
