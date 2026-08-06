using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <remarks>
    /// The server builds this type out of its own container, so a constructor
    /// argument is resolved from there rather than reached for through a static.
    /// </remarks>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">The logger.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Playback Statistics";

    /// <inheritdoc />
    /// <remarks>
    /// This value and the <c>guid</c> in build.yaml are one identity and have to
    /// stay equal. It is what a server and a catalogue tell this plugin apart
    /// by, and until this change both files carried the upstream template's.
    /// </remarks>
    public override Guid Id => Guid.Parse("29e90267-52ee-4bec-b4fb-870b8f5ddc53");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// The server's uninstall deletes the folder the assembly was installed
    /// into and nothing else, so without this the plugin's own data folder and
    /// its configuration file stay on disk after the plugin is gone. A plugin
    /// that records what people watched and then leaves that behind has made
    /// its own removal meaningless, which is why the hook is taken.
    /// <para>
    /// The paths are read off the base class here and passed on as values, so
    /// the removal itself is a function a test drives over a temporary
    /// directory rather than something only a running server can reach.
    /// </para>
    /// </remarks>
    public override void OnUninstalling()
    {
        PluginDataRemoval.Remove(DataFolderPath, ConfigurationFilePath, AssemblyFilePath, _logger);

        base.OnUninstalling();
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
