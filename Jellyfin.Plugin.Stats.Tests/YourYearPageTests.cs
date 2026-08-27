// The address the wrap-up page asks at, and the name the server knows it by,
// read off both sides rather than trusted. Issue #67.
//
// The page module is JavaScript and the route it has to agree with is C#, so
// nothing compiles the two together and nothing would say a word if they parted.
// What the module does with the two answers is driven by the node suite beside
// it; this file is about the address being the right one.

using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class YourYearPageTests
{
    private const string Module = "Jellyfin.Plugin.Stats/Pages/yourYearPage.js";

    /// <summary>
    /// The page asks the route the years are served at. A path that no longer
    /// resolves is a page that draws a failure notice on every load, and only a
    /// person opening it would notice.
    /// </summary>
    [Fact]
    public void ThePageAsksTheAddressTheYearsAreServedAt()
    {
        var controller = File.ReadAllText("Jellyfin.Plugin.Stats/Api/YourYearController.cs".Repositioned());
        var route = Match(controller, @"\[Route\(""([^""]+)""\)\]");
        var module = File.ReadAllText(Module.Repositioned());

        Assert.Equal(route, Match(module, @"export const YEARS_PATH = '([^']+)';"));
    }

    /// <summary>
    /// The assembled page is one the server is told about. A page nobody
    /// declares is embedded, unreachable and invisible.
    /// </summary>
    [Fact]
    public void TheWrapUpPageIsDeclaredToTheServer()
    {
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

        var page = Assert.Single(plugin.GetPages(), declared => declared.Name == Plugin.YourYearPage);

        Assert.Equal(typeof(Plugin).Namespace + ".Pages.yourYear.html", page.EmbeddedResourcePath);
    }

    /// <summary>
    /// Reads the one group of the one match, and refuses anything else.
    /// </summary>
    /// <param name="text">What to read.</param>
    /// <param name="pattern">What to look for.</param>
    /// <returns>The captured text.</returns>
    private static string Match(string text, string pattern)
    {
        var found = Regex.Matches(text, pattern);

        Assert.True(found.Count == 1, "The pattern " + pattern + " matched " + found.Count + " times, and a comparison against none or against several proves nothing.");

        return found[0].Groups[1].Value;
    }
}
