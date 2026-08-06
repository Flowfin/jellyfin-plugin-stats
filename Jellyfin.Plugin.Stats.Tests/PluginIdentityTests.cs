using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The plugin's identifier is written in two files: in <c>Plugin.Id</c>, which is
/// what a server tells installed plugins apart by, and in build.yaml, which is
/// what the packaging tool stamps into the package and what a catalogue lists.
/// Nothing in the compiler compares them, so an edit to either one alone builds,
/// packages and installs, and the plugin a catalogue offers is then a different
/// plugin from the one already on a server.
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
