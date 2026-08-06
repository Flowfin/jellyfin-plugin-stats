using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The configuration page is embedded by name and looked up by a name the plugin
/// builds at run time out of its own namespace. Nothing in the compiler checks
/// that those two agree, so a rename, a moved file or a changed root namespace
/// breaks the page and still builds and packages cleanly. This is that check.
/// </summary>
public class ConfigurationPageTests
{
    [Fact]
    public void TheConfigurationPageIsEmbeddedUnderTheNameThePluginLooksItUpBy()
    {
        var pluginType = typeof(Plugin);

        // The same expression Plugin.GetPages builds its EmbeddedResourcePath
        // from, so this fails for the same reason the page would fail to load.
        var expected = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            pluginType.Namespace);

        var embedded = pluginType.Assembly.GetManifestResourceNames();

        Assert.Contains(expected, embedded);
    }

    /// <summary>
    /// The page asks the server for a configuration by identifier, on the load
    /// and again on the save, so an identifier that is not the plugin's makes
    /// both calls read and write somebody else's settings or nothing at all.
    /// The assembly and the manifest are already compared to each other; this is
    /// the third side of that triangle, and it is the side nothing was watching
    /// when the other two were corrected.
    /// </summary>
    [Fact]
    public void ThePageAsksTheServerForThisPluginsConfiguration()
    {
        var declared = Regex.Match(
            EmbeddedConfigurationPage(),
            @"pluginUniqueId:\s*'(?<guid>[^']*)'");

        Assert.True(declared.Success, "The embedded page declares no quoted pluginUniqueId.");

        var text = declared.Groups["guid"].Value;
        Assert.True(
            Guid.TryParse(text, out var pageId),
            "The identifier in the embedded page does not parse: '" + text + "'.");

        Assert.Equal(PluginId(), pageId);
    }

    /// <summary>
    /// Reads the page out of the compiled assembly rather than the file beside
    /// it, because the embedded copy is the one a server loads and a file the
    /// build no longer embeds would still be sitting on disk and still pass.
    /// </summary>
    /// <returns>The text of the embedded configuration page.</returns>
    private static string EmbeddedConfigurationPage()
    {
        var pluginType = typeof(Plugin);
        var name = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            pluginType.Namespace);

        using var stream = pluginType.Assembly.GetManifestResourceStream(name);
        Assert.True(stream is not null, "No embedded resource is named " + name + ".");

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the identifier the compiled plugin returns, without a server to
    /// build it from. The property reads no instance state, so the object is
    /// made without running the constructor; one that later starts reading
    /// state fails here rather than passing quietly.
    /// </summary>
    /// <returns>The value of the plugin's identifier property.</returns>
    private static Guid PluginId()
    {
        var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        return plugin.Id;
    }
}
