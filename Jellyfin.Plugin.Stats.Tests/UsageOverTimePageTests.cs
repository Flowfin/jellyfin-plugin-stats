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
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class UsageOverTimePageTests
{
    private const string Module = "Jellyfin.Plugin.Stats/Pages/usageOverTimePage.js";

    private const string View = "Jellyfin.Plugin.Stats/Pages/usageOverTime.js";

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
    /// The second condition of issue #158. The answer that issue's decision
    /// settled is written where a reader of a report meets it, and it is true of
    /// the fold that produces the figures the reader is looking at.
    /// </summary>
    /// <remarks>
    /// A row holds two accounts of how a play was delivered: the method the
    /// server reported when it began, and the summary folded from every sample
    /// that arrived while it ran. They can disagree, and neither is wrong. The
    /// delivery figures under the range view come from the first of the two, so a
    /// reader who is not told which moment they are about reads a disagreement
    /// into figures that do not disagree - which is the defect that issue opened
    /// on, met by somebody guessing.
    /// <para>
    /// Two halves are asserted together on purpose. The sentence being present is
    /// the condition; the fold following the start is what makes the sentence
    /// true. Either one alone would leave the other free to move: a sentence with
    /// no behaviour behind it becomes a lie the day the fold changes, and a fold
    /// nobody describes is the state this issue was opened against.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheViewSaysWhichMomentItsDeliveryFiguresSpeakAboutAndTheFoldAgrees()
    {
        var sentence = DeclaredIn<string>(View, "DELIVERY_IS_READ_AT_THE_START", @"'([^']+)'", value => value);

        Assert.Contains("when it began", sentence, StringComparison.Ordinal);

        var changedPartway = APlayThatBeganAsADirectPlayAndWasReEncodedLater();

        Assert.NotNull(changedPartway.PlayMethodChangedUtc);
        Assert.False(changedPartway.Transcode!.VideoWasDirect);

        var shares = DeliveryMethodShares.Over(new[] { changedPartway });

        Assert.Equal(1, shares.DirectPlay);
        Assert.Equal(0, shares.Transcode);
    }

    /// <summary>
    /// One play of the shape the sentence above describes to a reader.
    /// </summary>
    /// <returns>A play that began as a direct play and was re-encoded partway through.</returns>
    private static PlayRecord APlayThatBeganAsADirectPlayAndWasReEncodedLater() => new()
    {
        SchemaVersion = 1,
        UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Movie",
        ParentId = null,
        ItemName = "A film",
        ItemRuntime = TimeSpan.FromMinutes(100),
        ChannelName = null,
        StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
        EndedUtc = new DateTime(2026, 3, 14, 10, 40, 0, DateTimeKind.Utc),
        WatchedDuration = TimeSpan.FromMinutes(100),
        ReachedTheEnd = true,
        ClientName = "Jellyfin Web",
        DeviceId = "device-1",
        DeviceName = "A browser",
        PlayMethodAtStart = PlayMethod.DirectPlay,
        PlayMethodChangedUtc = new DateTime(2026, 3, 14, 9, 1, 0, DateTimeKind.Utc),
        ClosedBy = PlayClosedBy.AStopEvent,
        Transcode = new TranscodeSummary
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            VideoWasDirect = false,
            AudioWasDirect = false,
            PeakBitrate = 8_000_000,
            TypicalBitrate = 6_000_000,
            HardwareAcceleration = null,
            Reasons = new[] { "VideoCodecNotSupported" }
        }
    };

    /// <summary>
    /// Reads a constant the page module declares.
    /// </summary>
    /// <typeparam name="T">What the constant holds.</typeparam>
    /// <param name="name">The constant's name in the module.</param>
    /// <param name="shape">How its value is written there.</param>
    /// <param name="read">Turns the text into the value.</param>
    /// <returns>The value the module declares.</returns>
    private static T Declared<T>(string name, string shape, Func<string, T> read)
        => DeclaredIn(Module, name, shape, read);

    /// <summary>
    /// Reads a constant one of the page's modules declares.
    /// </summary>
    /// <typeparam name="T">What the constant holds.</typeparam>
    /// <param name="module">The module, from the top of the repository.</param>
    /// <param name="name">The constant's name in the module.</param>
    /// <param name="shape">How its value is written there.</param>
    /// <param name="read">Turns the text into the value.</param>
    /// <returns>The value the module declares.</returns>
    private static T DeclaredIn<T>(string module, string name, string shape, Func<string, T> read)
    {
        var text = File.ReadAllText(module.Repositioned());

        return read(Match(text, @"export const " + name + @"\s*=\s*" + shape + ";"));
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
