using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
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
    /// <summary>
    /// What the server calls the page the usage view is drawn on.
    /// </summary>
    /// <remarks>
    /// The name is how a page is asked for: every plugin page is served from one
    /// address with the page named in the query, so this string is half of the
    /// only address this view has. It is named here rather than written into the
    /// page list below because the suite reads it back, and a page nobody can
    /// name is a page nobody can open.
    /// </remarks>
    public const string UsageOverTimePage = "Stats: usage over time";

    private readonly ILogger<Plugin> _logger;

    private bool _configurationMigrated;

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
    /// The stored configuration is moved to the shape this build reads here.
    /// This is the seam and not a convenient place: the base class reads this
    /// name to work out where the file is, immediately before opening it, and
    /// that is the last moment at which an old shape can still be repaired. By
    /// the time the file has been through the server's serializer, every
    /// element the current type has no property for has already been dropped,
    /// which is exactly the value a renamed setting still lives in.
    /// <para>
    /// The two neighbouring places both fail, measured rather than assumed on
    /// both supported server lines. The constructor is too early, because until
    /// the server has told the plugin where its assembly is, this name is
    /// derived from the entry assembly and points at the host's file rather
    /// than at <c>Jellyfin.Plugin.Stats.xml</c>. The call that tells it,
    /// <c>SetAttributes</c>, is sealed and cannot be extended.
    /// </para>
    /// <para>
    /// It runs once for the file, and the flag is set only where there was a
    /// file to deal with. Setting it on an absent file would spend the single
    /// attempt on a caller that only wanted to know the path, and the migration
    /// would then never run on the load that follows.
    /// </para>
    /// </remarks>
    public override string ConfigurationFileName
    {
        get
        {
            var path = StoredConfigurationPath;

            // Path.Exists rather than File.Exists, for the same reason the
            // removal uses it: a directory sitting where the settings file
            // belongs is a fault an administrator has to be told about, and
            // File.Exists is false for one, so guarding on it would pass over
            // that case without a word.
            if (!_configurationMigrated && Path.Exists(path))
            {
                _configurationMigrated = true;
                MigrateStoredConfiguration(path);
            }

            return base.ConfigurationFileName;
        }
    }

    /// <summary>
    /// Gets where the stored settings file is, without going through the
    /// migration above.
    /// </summary>
    /// <remarks>
    /// The base class works this out from <see cref="ConfigurationFileName"/>,
    /// so anything reading its <c>ConfigurationFilePath</c> runs the migration.
    /// That is right on the load and wrong everywhere else, and this is what
    /// the everywhere else uses.
    /// </remarks>
    private string StoredConfigurationPath => Path.Combine(ApplicationPaths.PluginConfigurationsPath, base.ConfigurationFileName);

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
        // Not through ConfigurationFilePath. That reads the name above, which
        // moves the stored file to the current shape, and rewriting a settings
        // file in the moment before deleting it is work that can only fail and
        // a line on the log about an upgrade nobody performed.
        PluginDataRemoval.Remove(DataFolderPath, StoredConfigurationPath, AssemblyFilePath, _logger);

        base.OnUninstalling();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The one place this plugin's configuration is written. Measured rather
    /// than assumed: on both supported server lines the no-argument save and
    /// <see cref="BasePlugin{TConfigurationType}.UpdateConfiguration"/> both
    /// come through here, so one guard covers all three ways in and there is no
    /// second copy of it to fall out of step.
    /// <para>
    /// Two things are refused here and they are refused in this order. The
    /// first reads the argument and the second reads the file, so a caller who
    /// sent a value nobody accepts is told about the value rather than about a
    /// file they cannot see.
    /// </para>
    /// <para>
    /// The value guard is the write half of a decision the model does not make
    /// on its own: a stored file carrying a bad value still loads, with that one
    /// field on its default, because a settings file must not stop a server from
    /// starting. A save carrying one is refused whole. Nothing here re-derives
    /// what is acceptable; the setters have already recorded which fields they
    /// refused, and this reads that.
    /// </para>
    /// </remarks>
    /// <param name="configuration">The configuration to write.</param>
    /// <exception cref="ConfigurationValueRefusedException">A value in <paramref name="configuration"/> is outside what this plugin accepts.</exception>
    /// <exception cref="ConfigurationIsNewerThanThePluginException">The stored file was written by a later version of this plugin.</exception>
    public override void SaveConfiguration(PluginConfiguration configuration)
    {
        var refused = configuration.RejectedFields;

        if (refused.Length > 0)
        {
            _logger.LogError(
                "A save was refused. The value sent for {Fields} is outside what this plugin accepts, so nothing was written and the stored settings are unchanged.",
                string.Join(", ", refused));

            throw new ConfigurationValueRefusedException(refused);
        }

        var stored = ConfigurationMigrator.VersionOfFile(ConfigurationFilePath, ConfigurationMigrations.Current);

        if (stored > ConfigurationMigrations.Current)
        {
            throw new ConfigurationIsNewerThanThePluginException(stored, ConfigurationMigrations.Current);
        }

        base.SaveConfiguration(configuration);
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
            },
            new PluginPageInfo
            {
                Name = UsageOverTimePage,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Pages.usageOverTime.html", GetType().Namespace)
            }
        ];
    }

    /// <summary>
    /// Moves the stored configuration file to the shape this build reads.
    /// </summary>
    /// <remarks>
    /// Nothing here is allowed out. A plugin whose configuration file cannot be
    /// migrated is a plugin with the wrong settings, and a server that will not
    /// start is a worse answer to that than a server that starts and says so on
    /// the log. Each case is reported at the level that matches what an
    /// operator can do about it.
    /// </remarks>
    /// <param name="path">The stored configuration file, which exists.</param>
    private void MigrateStoredConfiguration(string path)
    {
        try
        {
            var from = ConfigurationMigrator.MigrateFile(path, ConfigurationMigrations.All);

            // Once, on the start that moved the file, and never again, because
            // the file is at the current version from here on. A line on every
            // start would say nothing and would be read as if it had.
            //
            // The level is asked before the sentence describing the steps is
            // built. Below warning level the analyzers refuse work done for a
            // message that may never be written, and on a server with an
            // information level nobody turned on this is the whole cost of the
            // line.
            if (from is not null && _logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "The stored configuration was written in shape version {StoredVersion} and has been moved to version {PluginVersion}: {Changes}.",
                    from.Value,
                    ConfigurationMigrations.Current,
                    ConfigurationMigrations.Describe(from.Value));
            }
        }
        catch (ConfigurationIsNewerThanThePluginException ex)
        {
            // Left exactly as it is. This is the downgrade case, and the file
            // may hold settings this build has no property for; the server's
            // own writer would drop every one of them the first time anything
            // saved. The write guard refuses that save as well.
            _logger.LogError(
                "The stored configuration is at shape version {StoredVersion} and this plugin writes version {PluginVersion}. It was written by a later version of this plugin, so it is left as it is and settings cannot be saved until this plugin is upgraded again or the file is removed.",
                ex.StoredVersion,
                ex.PluginVersion);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(
                ex,
                "The stored configuration at {Path} could not be moved to shape version {PluginVersion}. The plugin is running on whatever the server was able to read from it.",
                path,
                ConfigurationMigrations.Current);
        }
    }
}
