// The two numbers and the one address the usage page has to agree with the rest
// of the plugin about, read off both sides rather than trusted. Issue #57.
//
// The page module is JavaScript and the things it has to agree with are C#, so
// nothing compiles the two together and nothing would say a word if they parted.
// Each case here reads the module's own declaration out of the tracked file and
// compares it against the value the plugin enforces, which is the only shape
// available for an agreement that spans two languages.
//
// What the module itself does with these values is driven by the node suite
// beside it, where it is a function from values to markup. This file is about
// the values being the right ones.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class UsageOverTimePageTests
{
    private const string Module = "Jellyfin.Plugin.Stats/Pages/usageOverTimePage.js";

    /// <summary>
    /// The second condition of issue #57. The range control is bounded by the
    /// same cap the query layer enforces, and the page states it.
    /// </summary>
    /// <remarks>
    /// The layer refuses a longer range before it opens anything, so a page
    /// carrying a larger number would offer a reader a range that comes back as
    /// a refusal rather than as a shorter report. A smaller one is a different
    /// defect and equally wrong: it would hide half of what the plugin answers
    /// over, and nothing would ever say so.
    /// </remarks>
    [Fact]
    public void ThePageStatesTheSameBoundTheQueryLayerEnforces()
    {
        Assert.Equal(
            (int)QueryWindow.LongestRangeAnyShapeAnswers.TotalDays,
            Declared<int>("LONGEST_RANGE_IN_DAYS", @"(\d+)", int.Parse));
    }

    /// <summary>
    /// The page asks the address the aggregate controller serves the days at.
    /// A path that no longer resolves is a page that draws a failure notice on
    /// every load, and nothing but a person opening it would notice.
    /// </summary>
    [Fact]
    public void ThePageAsksTheAddressTheDaysAreServedAt()
    {
        var controller = File.ReadAllText("Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs".Repositioned());
        var route = Match(controller, @"\[Route\(""([^""]+)""\)\]");
        var action = Match(controller, @"\[HttpGet\(""(Usage)""\)\]");

        Assert.Equal(route + "/" + action, Declared<string>("USAGE_PATH", @"'([^']+)'", value => value));
    }

    /// <summary>
    /// The assembled page is one the server is told about. A page nobody
    /// declares is embedded, unreachable and invisible, which is the one failure
    /// this whole route exists to remove.
    /// </summary>
    [Fact]
    public void TheAssembledPageIsDeclaredToTheServer()
    {
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

        var page = Assert.Single(plugin.GetPages(), declared => declared.Name == Plugin.UsageOverTimePage);

        Assert.Equal(typeof(Plugin).Namespace + ".Pages.usageOverTime.html", page.EmbeddedResourcePath);
    }

    /// <summary>
    /// Reads a constant the page module declares.
    /// </summary>
    /// <typeparam name="T">What the constant holds.</typeparam>
    /// <param name="name">The constant's name in the module.</param>
    /// <param name="shape">How its value is written there.</param>
    /// <param name="read">Turns the text into the value.</param>
    /// <returns>The value the module declares.</returns>
    private static T Declared<T>(string name, string shape, Func<string, T> read)
    {
        var module = File.ReadAllText(Module.Repositioned());

        return read(Match(module, @"export const " + name + @" = " + shape + ";"));
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

/// <summary>
/// Turns a path written from the top of the repository into one this suite can
/// open, which runs out of its own bin directory.
/// </summary>
internal static class RepositoryPaths
{
    /// <summary>
    /// Resolves a repository-relative path.
    /// </summary>
    /// <param name="path">The path, from the top of the repository.</param>
    /// <returns>The full path.</returns>
    public static string Repositioned(this string path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "No build.yaml was found above " + AppContext.BaseDirectory + ".");

        return Path.Combine(directory!.FullName, path.Replace('/', Path.DirectorySeparatorChar));
    }
}
