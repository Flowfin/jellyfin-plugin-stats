// How far a play got through its item, and the plays that have no such figure.
//
// The failure written against here is a number that looks real and is not. A
// completion computed over a live television channel, a photograph or a row the
// server gave no runtime for is arithmetic on a length that does not exist, and
// what comes out of it is not obviously wrong: a channel left on all evening
// reads as a programme somebody abandoned after two per cent, and it drags an
// average down with it.
//
// So two things are asserted together and neither is worth much alone. Such a
// play has no completion rather than a completion of nought, and a report over
// completion says how many rows it left out, because a share printed without
// that count reads as a statement about every play in the range.
//
// The third condition of issue #40 is the one that decides what happens to an
// item kind nobody here has classified. It is asserted by walking the server's
// own enum and asking this build for an answer about every member, and the
// bound on that walk is written on the case itself: it reaches the floor in
// Directory.Build.props and never a server in the field that is newer.
//
// Every row is built in memory and no clock, zone setting or store is touched.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class CompletionTests
{
    private static readonly DateTime Noon = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The item kinds the server this build compiles against can report. Read
    /// off the enum rather than listed here, for the reason the transcode
    /// reason walk gives: a list in a test is a second place for the set to be
    /// wrong.
    /// </summary>
    /// <returns>One entry per member of the enum this build carries.</returns>
    public static TheoryData<string> EveryItemKind()
    {
        var kinds = new TheoryData<string>();
        foreach (var name in Enum.GetNames<BaseItemKind>())
        {
            kinds.Add(name);
        }

        return kinds;
    }

    /// <summary>
    /// The third condition of issue #40. Every kind the server can report is
    /// classified by this build, so a kind nobody decided about is a failing
    /// run rather than a row that quietly stops being counted.
    /// </summary>
    /// <remarks>
    /// WHAT THIS FIRES ON AND WHAT IT CANNOT SEE. The kinds are an enum in the
    /// server's own package, so this compares the classification against the
    /// enum THIS BUILD COMPILES AGAINST, which is the floor named in
    /// Directory.Build.props for the framework the suite is running on. A
    /// member that appears when that floor is raised fails here. A server in
    /// the field newer than the floor, reporting a kind nobody has compiled
    /// against, is NOT caught here, and no build failure is available over an
    /// enum somebody else owns: an exhaustive switch does not compile at all,
    /// and the discard arm that makes one build is the silent gap this case
    /// exists against. That case is answered by what the code does with it,
    /// which the case below this one asserts.
    /// </remarks>
    /// <param name="kind">The kind the server can report.</param>
    [Theory]
    [MemberData(nameof(EveryItemKind))]
    public void EveryItemKindTheServerCanReportIsAccountedFor(string kind)
    {
        Assert.True(
            Completion.IsAccountedFor(kind),
            "Jellyfin.Plugin.Stats/Aggregation/Completion.cs classifies neither way for "
                + kind
                + ", so a play of one is left out of every completion figure and nobody decided that it should be.");
    }

    /// <summary>
    /// What the classification says, rather than only that it says something.
    /// The case above passes over a table that has answered every kind wrongly,
    /// so the two lists below name the kinds whose answer a reader of issue #40
    /// would check first, and a kind moved between them fails here.
    /// </summary>
    [Fact]
    public void TheKindsThatAreWatchedEndToEndAreTheOnesWithACompletion()
    {
        var endToEnd = new[] { "Audio", "AudioBook", "Episode", "Movie", "MusicVideo", "Recording", "Trailer", "Video" };
        var withNoLength = new[] { "Book", "LiveTvChannel", "LiveTvProgram", "Photo", "Program", "Series", "TvChannel", "TvProgram" };

        var wrong = endToEnd.Where(kind => !Completion.CanBeComputedFor(kind))
            .Concat(withNoLength.Where(Completion.CanBeComputedFor))
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "These kinds are on the wrong side of the classification in Completion.cs: " + string.Join(", ", wrong));
    }

    /// <summary>
    /// The first condition of issue #40, over the row the title of that issue
    /// is about. A play of an item with no runtime has no completion, and the
    /// distinction being kept is against nought rather than against an error.
    /// </summary>
    [Fact]
    public void APlayOfAnItemWithNoRuntimeHasNoCompletionRatherThanNought()
    {
        var play = APlay(itemType: "Audio", runtime: null, watched: TimeSpan.FromMinutes(9));

        Assert.Null(Completion.Of(play));
    }

    /// <summary>
    /// Live television is the case in the title of issue #40 and it is the one
    /// a rule about runtimes alone would get wrong. A programme carries the
    /// length it was scheduled for, so the arithmetic succeeds and answers a
    /// question nobody asked: somebody who joined a channel for ten minutes of
    /// a half hour programme did not abandon it a third of the way through.
    /// </summary>
    [Fact]
    public void LiveTelevisionHasNoCompletionEvenWhenTheServerGaveARuntime()
    {
        var play = APlay(itemType: "LiveTvProgram", runtime: TimeSpan.FromMinutes(30), watched: TimeSpan.FromMinutes(10));

        Assert.Null(Completion.Of(play));
    }

    /// <summary>
    /// The answer for a kind this build has never seen, which the third
    /// condition of issue #40 says has to come from what the code does rather
    /// than from a failing build. It is left out, and the report says so, which
    /// is the answer that cannot produce a figure nobody classified.
    /// </summary>
    [Fact]
    public void AnItemKindThisBuildHasNeverSeenIsLeftOutRatherThanCounted()
    {
        var play = APlay(itemType: "AKindNoServerHereHasEverReported", runtime: TimeSpan.FromMinutes(30), watched: TimeSpan.FromMinutes(15));

        Assert.Null(Completion.Of(play));

        var breakdown = CompletionBreakdown.Over(new[] { play });

        Assert.Equal(1, breakdown.PlaysLeftOut);
        Assert.Equal(1, breakdown.PlaysOfAKindWithNoLength);
        Assert.Null(breakdown.AverageCompletion);
    }

    /// <summary>
    /// The share itself, on the ordinary row, computed here the way a reader of
    /// the issue would compute it rather than by calling the same code twice.
    /// </summary>
    [Fact]
    public void TheShareIsTheWatchedTimeOverTheItemsLength()
    {
        var play = APlay(itemType: "Movie", runtime: TimeSpan.FromMinutes(100), watched: TimeSpan.FromMinutes(25));

        Assert.Equal(0.25, Completion.Of(play));
    }

    /// <summary>
    /// Somebody who rewinds watches more minutes than the film is long. That is
    /// a whole film rather than one and a half, because a share above one in an
    /// average makes the average say less than nothing.
    /// </summary>
    [Fact]
    public void APlayWatchedForLongerThanTheItemIsWholeRatherThanMoreThanWhole()
    {
        var play = APlay(itemType: "Movie", runtime: TimeSpan.FromMinutes(100), watched: TimeSpan.FromMinutes(150));

        Assert.Equal(1, Completion.Of(play));
    }

    /// <summary>
    /// A row this build did not write, carrying a watched duration below
    /// nought. Nought is the closest true thing to say about it, and it is said
    /// rather than a negative share being carried into an average.
    /// </summary>
    [Fact]
    public void ARowWatchedForLessThanNoTimeIsReadAsNought()
    {
        var play = APlay(itemType: "Movie", runtime: TimeSpan.FromMinutes(100), watched: TimeSpan.FromMinutes(-5));

        Assert.Equal(0, Completion.Of(play));
    }

    /// <summary>
    /// The second condition of issue #40. The figure and the count of what it
    /// left out arrive together, and the two exclusions are told apart because
    /// they are different things to whoever reads them: one is a kind that will
    /// never have a share, the other is a server that said nothing about a
    /// length.
    /// </summary>
    [Fact]
    public void AReportOverCompletionSaysHowManyRowsItLeftOut()
    {
        var plays = new[]
        {
            APlay(itemType: "Movie", runtime: TimeSpan.FromMinutes(100), watched: TimeSpan.FromMinutes(50)),
            APlay(itemType: "Episode", runtime: TimeSpan.FromMinutes(40), watched: TimeSpan.FromMinutes(40)),
            APlay(itemType: "LiveTvChannel", runtime: null, watched: TimeSpan.FromHours(3)),
            APlay(itemType: "Photo", runtime: null, watched: TimeSpan.FromSeconds(4)),
            APlay(itemType: "Movie", runtime: null, watched: TimeSpan.FromMinutes(12)),
        };

        var breakdown = CompletionBreakdown.Over(plays);

        Assert.Equal(5, breakdown.Plays);
        Assert.Equal(2, breakdown.PlaysWithACompletion);
        Assert.Equal(2, breakdown.PlaysOfAKindWithNoLength);
        Assert.Equal(1, breakdown.PlaysWithNoRuntime);
        Assert.Equal(3, breakdown.PlaysLeftOut);
        Assert.Equal(0.75, breakdown.AverageCompletion);
    }

    /// <summary>
    /// The average is over the rows that had a share and never over the rows
    /// that did not. Folding the left-out rows in as noughts is the arithmetic
    /// this whole file exists against, and it is asserted as a number rather
    /// than left to the reading of the counts above.
    /// </summary>
    [Fact]
    public void TheRowsLeftOutDoNotDragTheAverageDown()
    {
        var watched = APlay(itemType: "Movie", runtime: TimeSpan.FromMinutes(100), watched: TimeSpan.FromMinutes(100));
        var channel = APlay(itemType: "LiveTvChannel", runtime: null, watched: TimeSpan.FromHours(5));

        Assert.Equal(1, CompletionBreakdown.Over(new[] { watched, channel }).AverageCompletion);
    }

    /// <summary>
    /// A range where nothing had a share says so, rather than answering nought.
    /// A nought there reads as everybody having stopped immediately.
    /// </summary>
    [Fact]
    public void AnAverageOverNoSuchRowIsAbsentRatherThanNought()
    {
        var breakdown = CompletionBreakdown.Over(new[] { APlay(itemType: "LiveTvChannel", runtime: null, watched: TimeSpan.FromHours(2)) });

        Assert.Null(breakdown.AverageCompletion);
        Assert.Equal(1, breakdown.Plays);
        Assert.Equal(0, breakdown.PlaysWithACompletion);
    }

    /// <summary>
    /// An empty fold answers rather than throwing, and says nothing about a
    /// range it was handed no rows for.
    /// </summary>
    [Fact]
    public void AFoldOverNothingCountsNothing()
    {
        var breakdown = CompletionBreakdown.Over(Array.Empty<PlayRecord>());

        Assert.Equal(0, breakdown.Plays);
        Assert.Equal(0, breakdown.PlaysLeftOut);
        Assert.Null(breakdown.AverageCompletion);
    }

    /// <summary>
    /// Neither entry point invents an answer for an argument that is not there.
    /// </summary>
    [Fact]
    public void NeitherReadingAcceptsNothingAtAll()
    {
        Assert.Throws<ArgumentNullException>(() => Completion.Of(null!));
        Assert.Throws<ArgumentNullException>(() => CompletionBreakdown.Over(null!));
    }

    private static PlayRecord APlay(
        string itemType,
        TimeSpan? runtime,
        TimeSpan watched) => new()
        {
            SchemaVersion = 1,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = itemType,
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = runtime,
            StartedUtc = Noon,
            EndedUtc = Noon.AddMinutes(41),
            WatchedDuration = watched,
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.DirectPlay,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
}
