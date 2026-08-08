using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The plugin's identifier is written in three files: in <c>Plugin.Id</c>, which
/// is what a server tells installed plugins apart by, in build.yaml, which is
/// what the packaging tool stamps into the package and what a catalogue lists,
/// and in the configuration page, which passes it to both of the calls it makes
/// to the server. Nothing in the compiler compares them, so an edit to any one
/// alone builds, packages and installs, and what breaks is either the plugin a
/// catalogue offers being a different plugin from the one already on a server,
/// or a settings page that reads and writes the configuration of a plugin that
/// is not this one.
/// </summary>
public class PluginIdentityTests
{
    [Fact]
    public void ThePluginIdParsesAndEqualsTheGuidInTheManifest()
    {
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "build.yaml"));

        var declaration = Regex.Match(manifest, "(?m)^guid:[ ]*\"(?<guid>[^\"]*)\"");
        Assert.True(declaration.Success, "build.yaml carries no quoted guid line.");

        var declared = declaration.Groups["guid"].Value;
        Assert.True(
            Guid.TryParse(declared, out var manifestId),
            "The guid in build.yaml does not parse as a identifier: '" + declared + "'.");

        Assert.Equal(manifestId, PluginId());
    }

    [Fact]
    public void ThePluginIdEqualsTheIdentifierTheConfigurationPageAsksTheServerFor()
    {
        var page = EmbeddedConfigurationPage();

        var declaration = Regex.Match(page, "pluginUniqueId:[ ]*'(?<guid>[^']*)'");
        Assert.True(declaration.Success, "The configuration page carries no quoted pluginUniqueId.");

        var declared = declaration.Groups["guid"].Value;
        Assert.True(
            Guid.TryParse(declared, out var pageId),
            "The pluginUniqueId in the configuration page does not parse as an identifier: '" + declared + "'.");

        Assert.Equal(pageId, PluginId());
    }

    /// <summary>
    /// Reads the configuration page out of the compiled plugin assembly.
    /// </summary>
    /// <remarks>
    /// The embedded copy is read rather than the file beside it, because the
    /// embedded copy is the one a server loads and serves to a dashboard. A page
    /// edited on disk and not rebuilt into the assembly is exactly the state this
    /// test exists to refuse, and reading the file would pass on it.
    /// </remarks>
    /// <returns>The text of the embedded configuration page.</returns>
    private static string EmbeddedConfigurationPage()
    {
        var pluginType = typeof(Plugin);

        // The same expression Plugin.GetPages builds its EmbeddedResourcePath
        // from, so a page this cannot find is a page the server cannot find.
        var name = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            pluginType.Namespace);

        using var stream = pluginType.Assembly.GetManifestResourceStream(name);
        Assert.True(stream is not null, "The assembly embeds no resource named " + name + ".");

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the identifier the compiled plugin returns.
    /// </summary>
    /// <remarks>
    /// Constructing the plugin needs the server's application paths, and the
    /// fakes for the server surfaces are not in this suite yet. The identifier
    /// reads no instance state, so the object is made without running the
    /// constructor and the property is read off it. An identifier that later
    /// starts reading state fails here rather than passing quietly.
    /// </remarks>
    /// <returns>The value of the plugin's identifier property.</returns>
    private static Guid PluginId()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        return plugin.Id;
    }

    /// <summary>
    /// Finds the directory holding the tracked build.yaml.
    /// </summary>
    /// <remarks>
    /// The tracked manifest is read rather than a copy of it, because a test over
    /// a copy proves the copy. The suite runs out of its own bin directory, so the
    /// walk climbs until it finds the file and gives up at the top of the volume.
    /// </remarks>
    /// <returns>The full path of the directory that holds build.yaml.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "No build.yaml was found above " + AppContext.BaseDirectory + ".");
        return directory!.FullName;
    }
}
