using System;
using System.Linq;
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
            "{0}.Configuration.configurationPage.html",
            pluginType.Namespace);

        var embedded = pluginType.Assembly.GetManifestResourceNames();

        Assert.Contains(expected, embedded);
    }
}
