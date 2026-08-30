// The four reads one person's own page draws from. Issue #274.
//
// Driven against a real store on disk, because what these conditions are about
// is which reads a window issues, what a store with no rollups answers, and what
// a read that refuses costs. A case over an in-memory sequence would pass over a
// store that never wrote a rollup and over an answer that read every row an
// account has.
//
// Berlin is the zone throughout, because a play late at night there belongs to
// the next day in UTC, so a window that fell back to UTC comes out a day off
// rather than passing quietly. The moment the window ends at is handed in rather
// than read off a clock, so a case says which day it is about.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class YourOwnFiguresTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private static readonly Guid Bob = new("22222222222222222222222222222222");

    /// <summary>
    /// The middle of a June day, which every window in this file is read back
    /// from.
    /// </summary>
    private static readonly DateTimeOffset Noon = new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;

    public YourOwnFiguresTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Each of the three windows exists, is grouped the way its name says, and
    /// answers with every part of itself.
    /// </summary>
    /// <remarks>
    /// The count of parts is the assertion rather than their contents, because
    /// what this holds is that the window is the one that was asked for: thirty
    /// days is thirty parts whatever was watched in them, and a series built
    /// from the days that have rows would be shorter on a quiet account and
    /// nobody could see that it was.
    /// </remarks>
    /// <param name="window">Which window.</param>
    /// <param name="name">What it is called on the wire.</param>
    /// <param name="parts">How many parts it has.</param>
    [Theory]
    [InlineData(PersonalWindow.Last30Days, "last30Days", 30)]
    [InlineData(PersonalWindow.Last12Months, "last12Months", 12)]
    [InlineData(PersonalWindow.AllTime, "allTime", 0)]
    public void EachWindowIsGroupedTheWayItsNameSaysAndCarriesEveryPartOfItself(
        PersonalWindow window,
        string name,
        int parts)
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(store, Ada, window, Berlin, Noon, topCount: 5);

        Assert.Equal(name, figures.Window);
        Assert.Equal(Berlin.Id, figures.ZoneId);
        Assert.Equal(parts, figures.Points.Count);
        Assert.Empty(figures.Degraded);
    }

    /// <summary>
    /// The plays, the watched time and the completion split agree with a
    /// straightforward count over the same rows, and the split adds up.
    /// </summary>
    /// <remarks>
    /// The figures come off the rollups and the count here comes off the rows,
    /// so this is two sources agreeing rather than one restated. That the
    /// finished and abandoned counts add up to the plays is asserted separately,
    /// because a fold that lost a row loses it from all three and the sum would
    /// still hold.
    /// </remarks>
    [Fact]
    public void TheFiguresAgreeWithTheSameCountTakenOverTheRows()
    {
        var plays = AJuneOfPlays(Ada).ToList();

        Seed(plays.Concat(AJuneOfPlays(Bob)).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        var inWindow = plays
            .Where(play => DayOf(play) >= DateOnly.FromDateTime(Noon.UtcDateTime).AddDays(-29))
            .Where(play => DayOf(play) <= DateOnly.FromDateTime(Noon.UtcDateTime))
            .ToList();

        Assert.Equal(inWindow.Count, figures.Plays);
        Assert.Equal(
            inWindow.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
            figures.Watched);
        Assert.Equal(inWindow.Count(play => play.ReachedTheEnd), figures.Finished);
        Assert.Equal(inWindow.Count(play => !play.ReachedTheEnd), figures.Abandoned);
        Assert.Equal(figures.Plays, figures.Finished + figures.Abandoned);
    }

    /// <summary>
    /// One account's window reads that account's rows and nobody else's.
    /// </summary>
    /// <remarks>
    /// The failure written against is the one that looks like a working answer:
    /// a window whose arithmetic is right and whose subject is the whole server.
    /// It is asserted from both ends, that the totals are this account's and that
    /// nothing only somebody else watched reaches the top list.
    /// </remarks>
    [Fact]
    public void OneAccountsWindowIsFoldedFromTheirOwnRowsAndNobodyElses()
    {
        var theirs = new Guid("44444444444444444444444444444444");

        Seed(AJuneOfPlays(Ada)
            .Concat(AJuneOfPlays(Bob).Select(play => play with { ItemId = theirs }))
            .ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var hers = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        var alone = AggregateQueries.TheirFiguresOver(
            store,
            Bob,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        Assert.Equal(AJuneOfPlays(Ada).Count(), hers.Plays);
        Assert.Equal(AJuneOfPlays(Bob).Count(), alone.Plays);
        Assert.DoesNotContain(hers.TopItems, row => row.Key == theirs);
        Assert.All(alone.TopItems, row => Assert.Equal(theirs, row.Key));
    }

    /// <summary>
    /// The top list holds the most watched up to the bound, longest first.
    /// </summary>
    [Fact]
    public void TheTopListHoldsTheMostWatchedUpToTheBoundAndNoMore()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 2);

        Assert.Equal(2, figures.TopItems.Count);
        Assert.True(figures.TopItems[0].Watched >= figures.TopItems[1].Watched);
        Assert.All(figures.TopItems, row => Assert.NotNull(row.Name));
    }

    /// <summary>
    /// A store that has never keyed a rollup answers the window in full, from
    /// the rows.
    /// </summary>
    /// <remarks>
    /// This is the case the fold was rebuilt for. The rows are read for the top
    /// items whatever happens, so a store with no usable aggregates costs
    /// nothing extra and degrades nothing: what a reader gets is the same window
    /// rather than a window saying it could not be taken.
    /// </remarks>
    [Fact]
    public void AStoreWithNoUsableRollupsStillAnswersTheWindowFromTheRows()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var fromRollups = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        var fromRows = OwnFiguresFold.Over(
            "last30Days",
            PersonalWindow.Last30Days,
            Berlin,
            DateOnly.FromDateTime(Noon.UtcDateTime).AddDays(-29),
            DateOnly.FromDateTime(Noon.UtcDateTime).AddDays(1),
            rollups: null,
            rows: AJuneOfPlays(Ada).ToList(),
            rowsRefusedBecause: null,
            topCount: 5);

        Assert.Equal(fromRollups.Plays, fromRows.Plays);
        Assert.Equal(fromRollups.Watched, fromRows.Watched);
        Assert.Equal(fromRollups.Finished, fromRows.Finished);
        Assert.Equal(fromRollups.Abandoned, fromRows.Abandoned);
        Assert.Equal(fromRollups.Points.Count, fromRows.Points.Count);
        Assert.Empty(fromRows.Degraded);
    }

    /// <summary>
    /// A store whose rollups were keyed in another zone is read from the rows
    /// rather than from days that are somebody else's midnights.
    /// </summary>
    [Fact]
    public void RollupsKeyedInAnotherZoneAreNotReadAsThisWindowsDays()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Auckland,
            Noon,
            topCount: 5);

        Assert.Equal(Auckland.Id, figures.ZoneId);
        Assert.NotNull(figures.Plays);
        Assert.Empty(figures.Degraded);
    }

    /// <summary>
    /// A window whose rows will not fit under the bound degrades exactly the
    /// figure only a row can carry, and the rest of the window stands.
    /// </summary>
    /// <remarks>
    /// This is the #66 rule met for a window rather than for a year: the reader
    /// cannot shorten a window this page offers, so a read that refuses costs
    /// the figure it would have fed and never the answer.
    /// </remarks>
    [Fact]
    public void AWindowOverTheBoundLosesTheTopListAndKeepsEverythingElse()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            new EveryWindowHoldsMoreThanAReadMay(store),
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        Assert.NotNull(figures.Plays);
        Assert.NotNull(figures.Watched);
        Assert.NotNull(figures.Finished);
        Assert.NotEmpty(figures.Points);
        Assert.Empty(figures.TopItems);
        Assert.True(figures.Degraded.ContainsKey(OwnFigures.TopItemsFigure));
        Assert.False(figures.Degraded.ContainsKey(OwnFigures.PlaysFigure));
    }

    /// <summary>
    /// Where neither source can be read, every figure is absent with its reason
    /// and none of them is nought.
    /// </summary>
    /// <remarks>
    /// A nought is a person who watched nothing. Once an unknown has been
    /// written as one no reader can tell them apart, and this is the case that
    /// holds the difference at the shape that would produce it.
    /// </remarks>
    [Fact]
    public void AWindowNeitherSourceCouldAnswerSaysSoRatherThanAnsweringWithNoughts()
    {
        var figures = OwnFiguresFold.Over(
            "allTime",
            PersonalWindow.AllTime,
            Berlin,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 16),
            rollups: null,
            rows: null,
            rowsRefusedBecause: "There are more of these than one answer may read.",
            topCount: 5);

        Assert.Null(figures.Plays);
        Assert.Null(figures.Watched);
        Assert.Null(figures.Finished);
        Assert.Null(figures.Abandoned);
        Assert.Empty(figures.Points);
        Assert.Empty(figures.TopItems);

        foreach (var figure in new[]
                 {
                     OwnFigures.PlaysFigure,
                     OwnFigures.WatchedFigure,
                     OwnFigures.CompletionFigure,
                     OwnFigures.TopItemsFigure,
                 })
        {
            Assert.True(figures.Degraded.ContainsKey(figure), $"{figure} is absent and says nothing about why.");
        }
    }

    /// <summary>
    /// A window reaching further back than the answer may read the rows over
    /// says so rather than issuing an unbounded number of bounded reads.
    /// </summary>
    /// <remarks>
    /// Bounded reads without a bound on how many of them there are is not a
    /// bounded answer, and that is the reading this refuses. The totals still
    /// stand, because they come off a read bounded by days.
    /// </remarks>
    [Fact]
    public void AWindowReachingBackFurtherThanTheRowsMayBeReadOverLosesOnlyTheTopList()
    {
        Seed(APlay(Ada, new DateTime(2020, 1, 5, 20, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30), true));

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.AllTime,
            Berlin,
            Noon,
            topCount: 5);

        Assert.True(figures.Degraded.ContainsKey(OwnFigures.TopItemsFigure));
        Assert.Empty(figures.TopItems);
        Assert.NotNull(figures.Plays);
    }

    /// <summary>
    /// All time over a store holding nothing is the day it is asked on rather
    /// than a window reaching back to nowhere.
    /// </summary>
    [Fact]
    public void AllTimeOverAStoreHoldingNothingIsTheDayItWasAskedOn()
    {
        Seed();

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.AllTime,
            Berlin,
            Noon,
            topCount: 5);

        Assert.Equal(0, figures.Plays);
        Assert.Empty(figures.TopItems);
        Assert.Empty(figures.Degraded);
    }

    /// <summary>
    /// An item whose name the server never gave is a row with no name rather
    /// than a row named with a blank.
    /// </summary>
    [Fact]
    public void AnItemWithNoNameIsARowCarryingNone()
    {
        Seed(APlay(Ada, Noon.UtcDateTime.AddDays(-1), TimeSpan.FromMinutes(20), true) with { ItemName = "   " });

        using var store = new SqlitePlayStore(_root, Berlin);

        var figures = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        Assert.Single(figures.TopItems);
        Assert.Null(figures.TopItems[0].Name);
    }

    /// <summary>
    /// The set of windows a request may name is the one the controller declares,
    /// and it is the set the page asks with.
    /// </summary>
    /// <remarks>
    /// Read back rather than repeated, so a fourth window added to one and not
    /// the other reddens here instead of arriving as a request nobody folds.
    /// </remarks>
    [Fact]
    public void EveryWindowTheEnumerationHoldsIsOneARequestMayName()
    {
        Assert.Equal(
            Enum.GetValues<PersonalWindow>().Length,
            YourStatisticsController.Windows.Members.Count);

        foreach (var member in YourStatisticsController.Windows.Members)
        {
            Assert.True(
                YourStatisticsController.Windows.TryMap(member.Key, out var mapped),
                $"{member.Key} is declared and does not map.");

            Assert.Equal(member.Value, mapped);
        }
    }

    /// <summary>
    /// A window this build has no name for is refused before the store is
    /// opened.
    /// </summary>
    /// <remarks>
    /// Nothing is opened, so the refusal is the same on every server rather than
    /// one that fires where a store happens to be missing. A number is here
    /// because that is the spelling issue #55 is named for: an enumeration bound
    /// straight out of a request takes one where it would refuse a word.
    /// <para>
    /// A DIFFERENTLY CASED SPELLING IS NOT REFUSED AND IS NOT LISTED HERE. The
    /// set matches its members with <c>OrdinalIgnoreCase</c>, so
    /// <c>Last30Days</c> is the same member as <c>last30Days</c>. That is the
    /// set's own decision rather than a hole in this one, and a case asserting
    /// otherwise would be asserting against the wrong file.
    /// </para>
    /// </remarks>
    /// <param name="window">What the request named.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("banana")]
    [InlineData("0")]
    [InlineData("lastFortnight")]
    public async Task AWindowThisBuildHasNoNameForIsRefusedAndNothingIsOpened(string window)
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new InvalidOperationException("Nothing should be opened.")));

        var who = Caller.Someone;
        var answer = await endpoints.Send("GET", $"/Stats/Users/{who.UserId}/Statistics/{window}", who);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is the plugin being unavailable rather than a
    /// window nobody watched anything in.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAnEmptyWindow()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new StoreCouldNotBeOpenedException()));

        var who = Caller.Someone;
        var answer = await endpoints.Send("GET", $"/Stats/Users/{who.UserId}/Statistics/allTime", who);

        Assert.Equal(503, answer.Status);
    }

    /// <summary>
    /// A window this build has no name for is refused rather than folded under a
    /// name somebody invented.
    /// </summary>
    /// <remarks>
    /// The guard is for the member added tomorrow. A fourth window whose name
    /// nobody wrote would otherwise reach the wire labelled with whatever the
    /// last arm happened to be, and the page would draw one window's figures
    /// under another window's heading.
    /// </remarks>
    [Fact]
    public void AWindowThisBuildHasNoNameForIsRefusedRatherThanFoldedAnyway()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AggregateQueries.TheirFiguresOver(
                store,
                Ada,
                (PersonalWindow)99,
                Berlin,
                Noon,
                topCount: 5));
    }

    /// <summary>
    /// A window holding more recorded days than one answer may read is folded
    /// from the rows rather than from the days that fitted.
    /// </summary>
    /// <remarks>
    /// A truncated fold is a figure wrong by whatever it did not read with
    /// nothing on it saying so. The rows answer instead and nothing is degraded,
    /// because they were being read anyway.
    /// </remarks>
    [Fact]
    public void AWindowOverTheRollupBoundIsFoldedFromTheRowsAndNotFromWhatFitted()
    {
        Seed(AJuneOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var honest = AggregateQueries.TheirFiguresOver(
            store,
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        var overTheBound = AggregateQueries.TheirFiguresOver(
            new EveryRollupReadIsOverTheBound(store),
            Ada,
            PersonalWindow.Last30Days,
            Berlin,
            Noon,
            topCount: 5);

        Assert.Equal(honest.Plays, overTheBound.Plays);
        Assert.Equal(honest.Watched, overTheBound.Watched);
        Assert.Equal(honest.Finished, overTheBound.Finished);
        Assert.Empty(overTheBound.Degraded);
    }

    /// <summary>
    /// The fold counts the window it was asked about rather than everything it
    /// was handed, from either source.
    /// </summary>
    /// <remarks>
    /// Both sources are handed rows outside the window on purpose. A fold that
    /// counted what it was given would widen the answer silently, and a reader
    /// would be shown a longer window than the one they asked for under the
    /// heading of a shorter one.
    /// </remarks>
    [Fact]
    public void TheFoldCountsOnlyTheWindowItWasAskedAboutFromEitherSource()
    {
        var firstDay = new DateOnly(2026, 6, 1);
        var dayAfter = new DateOnly(2026, 6, 11);

        var rows = AJuneOfPlays(Ada)
            .Concat(new[]
            {
                APlay(Ada, new DateTime(2026, 5, 1, 18, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(9), true),
            })
            .ToList();

        var fromRows = OwnFiguresFold.Over(
            "last30Days",
            PersonalWindow.Last30Days,
            Berlin,
            firstDay,
            dayAfter,
            rollups: null,
            rows: rows,
            rowsRefusedBecause: null,
            topCount: 5);

        Assert.Equal(AJuneOfPlays(Ada).Count(), fromRows.Plays);

        var rollups = new[]
        {
            ARollup(new DateOnly(2026, 6, 2), plays: 4, completed: 3, TimeSpan.FromHours(2)),
            ARollup(new DateOnly(2026, 5, 2), plays: 99, completed: 99, TimeSpan.FromHours(50)),
        };

        var fromRollups = OwnFiguresFold.Over(
            "last30Days",
            PersonalWindow.Last30Days,
            Berlin,
            firstDay,
            dayAfter,
            rollups: rollups,
            rows: rows,
            rowsRefusedBecause: null,
            topCount: 5);

        Assert.Equal(4, fromRollups.Plays);
        Assert.Equal(TimeSpan.FromHours(2), fromRollups.Watched);
        Assert.Equal(3, fromRollups.Finished);
        Assert.Equal(1, fromRollups.Abandoned);
    }

    /// <summary>
    /// A figure nobody supplied a reason for still says it could not be taken,
    /// in words of this fold's own.
    /// </summary>
    /// <remarks>
    /// A reason is the caller's to give and the absence is not. A figure that
    /// came back missing with an empty explanation reads as a figure nobody
    /// noticed, which is the state this fold exists to make impossible.
    /// </remarks>
    [Fact]
    public void AFigureWithNoReasonSuppliedStillSaysItCouldNotBeTaken()
    {
        var figures = OwnFiguresFold.Over(
            "allTime",
            PersonalWindow.AllTime,
            Berlin,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 6, 16),
            rollups: null,
            rows: null,
            rowsRefusedBecause: null,
            topCount: 5);

        Assert.NotEmpty(figures.Degraded[OwnFigures.TopItemsFigure]);
        Assert.NotEmpty(figures.Degraded[OwnFigures.PlaysFigure]);
        Assert.NotEmpty(figures.Degraded[OwnFigures.WatchedFigure]);
        Assert.NotEmpty(figures.Degraded[OwnFigures.CompletionFigure]);
    }

    /// <summary>
    /// A twelve month window puts each month's watching under that month and
    /// leaves the months nothing happened in reading nought.
    /// </summary>
    /// <remarks>
    /// Two months with rows rather than one, because a series that put every
    /// day under whichever month it walked last passes a case whose rows are all
    /// in one month, and the answer would then be a year-shaped chart with one
    /// bar in the wrong place. The empty months are asserted too: they are what
    /// separates the window that was asked for from the window that had rows.
    /// </remarks>
    [Fact]
    public void EachMonthOfATwelveMonthWindowHoldsItsOwnWatchingAndNobodyElses()
    {
        var figures = OwnFiguresFold.Over(
            "last12Months",
            PersonalWindow.Last12Months,
            Berlin,
            new DateOnly(2025, 7, 1),
            new DateOnly(2026, 6, 16),
            rollups: new[]
            {
                ARollup(new DateOnly(2025, 9, 4), plays: 2, completed: 2, TimeSpan.FromHours(3)),
                ARollup(new DateOnly(2026, 6, 4), plays: 5, completed: 1, TimeSpan.FromHours(7)),
            },
            rows: Array.Empty<PlayRecord>(),
            rowsRefusedBecause: null,
            topCount: 5);

        Assert.Equal(12, figures.Points.Count);
        Assert.Equal("2025-07", figures.Points[0].Label);
        Assert.Equal("2026-06", figures.Points[11].Label);

        var september = Assert.Single(figures.Points, point => point.Label == "2025-09");
        var june = Assert.Single(figures.Points, point => point.Label == "2026-06");

        Assert.Equal(TimeSpan.FromHours(3), september.Watched);
        Assert.Equal(TimeSpan.FromHours(7), june.Watched);
        Assert.Equal(TimeSpan.Zero, Assert.Single(figures.Points, point => point.Label == "2025-10").Watched);

        Assert.Equal(7, figures.Plays);
        Assert.Equal(TimeSpan.FromHours(10), figures.Watched);
    }

    private static DailyRollup ARollup(DateOnly day, long plays, long completed, TimeSpan watched)
        => new()
        {
            Day = day,
            UserId = Ada,
            ItemType = "Episode",
            ClientName = "Jellyfin Web",
            Plays = plays,
            Watched = watched,
            Completed = completed,
            UnknownMethod = 0,
            DirectPlay = plays,
            DirectStream = 0,
            Transcode = 0,
        };

    private static DateOnly DayOf(PlayRecord play)
        => LocalDay.Of(new DateTimeOffset(DateTime.SpecifyKind(play.StartedUtc, DateTimeKind.Utc)), Berlin);

    private static IEnumerable<PlayRecord> AJuneOfPlays(Guid who)
    {
        for (var day = 1; day <= 10; day++)
        {
            for (var play = 0; play < 3; play++)
            {
                yield return APlay(
                    who,
                    new DateTime(2026, 6, day, 18, 0, 0, DateTimeKind.Utc).AddHours(play),
                    TimeSpan.FromMinutes(20 + (play * 15)),
                    reachedTheEnd: play != 2,
                    itemId: new Guid($"5555555555555555555555555555555{play}"));
            }
        }
    }

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan watched,
        bool reachedTheEnd,
        Guid? itemId = null)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId ?? new Guid("66666666666666666666666666666666"),
            ItemType = "Episode",
            ParentId = new Guid("77777777777777777777777777777777"),
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = reachedTheEnd,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>(),
            },
        };

    private void Seed(params PlayRecord[] plays)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Berlin);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }

    /// <summary>
    /// A store whose every rollup read answers with one row more than the read
    /// asked to hold.
    /// </summary>
    /// <remarks>
    /// The rows underneath are the real ones, so what the fold falls back to is
    /// a real answer rather than a second fake.
    /// </remarks>
    private sealed class EveryRollupReadIsOverTheBound : IPlayStore
    {
        private readonly IPlayStore _behind;

        public EveryRollupReadIsOverTheBound(IPlayStore behind) => _behind = behind;

        public TimeZoneInfo? RollupZone => _behind.RollupZone;

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit)
            => Enumerable
                .Repeat(ARollup(fromDay, plays: 1, completed: 1, TimeSpan.FromMinutes(1)), limit)
                .ToList();

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
            => _behind.PlaysBetween(fromUtc, toUtc, limit);

        public void Add(PlayRecord play) => _behind.Add(play);

        public void NoteOpenPlay(OpenPlay play) => _behind.NoteOpenPlay(play);

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
            => _behind.AddAndForgetOpenPlay(play, playKey);

        public void ForgetOpenPlay(string playKey) => _behind.ForgetOpenPlay(playKey);

        public IEnumerable<OpenPlay> OpenPlays() => _behind.OpenPlays();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => _behind.MostRecentPlays(limit);

        public IEnumerable<PlayRecord> AllPlays() => _behind.AllPlays();

        public IEnumerable<DailyRollup> AllRollups() => _behind.AllRollups();

        public void RebuildRollups() => _behind.RebuildRollups();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => _behind.PlaysFor(userId);

        public IReadOnlyList<Guid> UserIdsWithPlays() => _behind.UserIdsWithPlays();

        public IReadOnlyList<Guid> UserIdsWithConsent() => _behind.UserIdsWithConsent();

        public DateTime? OldestPlayStartedUtc() => _behind.OldestPlayStartedUtc();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone)
            => _behind.YearsWithPlaysFor(userId, zone);

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => _behind.CountPlaysStartedBefore(cutoffUtc);

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysStartedBefore(cutoffUtc, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, fromUtc, toUtc, deletionClass, limit);

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => _behind.DeletionsRecorded(limit);

        public ConsentRecord? ConsentFor(Guid userId) => _behind.ConsentFor(userId);

        public void RecordConsent(ConsentRecord consent) => _behind.RecordConsent(consent);

        public void ForgetConsentFor(Guid userId) => _behind.ForgetConsentFor(userId);

        public void ReclaimFreedSpace() => _behind.ReclaimFreedSpace();

        public void Dispose() => _behind.Dispose();
    }

    /// <summary>
    /// A store whose every bounded read of rows answers with one row more than
    /// the read asked to hold, over one that holds real ones.
    /// </summary>
    /// <remarks>
    /// The refusal is produced by the bound rather than by a store that answers
    /// nothing, so the case reaches the same branch a real server over the cap
    /// would. Every other read goes to the store behind it, so the rollups are
    /// the real ones and the totals stand for a second reason than this fake.
    /// </remarks>
    private sealed class EveryWindowHoldsMoreThanAReadMay : IPlayStore
    {
        private readonly IPlayStore _behind;

        public EveryWindowHoldsMoreThanAReadMay(IPlayStore behind) => _behind = behind;

        public TimeZoneInfo? RollupZone => _behind.RollupZone;

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
            => Enumerable
                .Repeat(APlay(Ada, fromUtc, TimeSpan.FromMinutes(1), reachedTheEnd: true), limit)
                .ToList();

        public void Add(PlayRecord play) => _behind.Add(play);

        public void NoteOpenPlay(OpenPlay play) => _behind.NoteOpenPlay(play);

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
            => _behind.AddAndForgetOpenPlay(play, playKey);

        public void ForgetOpenPlay(string playKey) => _behind.ForgetOpenPlay(playKey);

        public IEnumerable<OpenPlay> OpenPlays() => _behind.OpenPlays();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => _behind.MostRecentPlays(limit);

        public IEnumerable<PlayRecord> AllPlays() => _behind.AllPlays();

        public IEnumerable<DailyRollup> AllRollups() => _behind.AllRollups();

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit)
            => _behind.RollupsFor(userId, fromDay, toDay, limit);

        public void RebuildRollups() => _behind.RebuildRollups();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => _behind.PlaysFor(userId);

        public IReadOnlyList<Guid> UserIdsWithPlays() => _behind.UserIdsWithPlays();

        public IReadOnlyList<Guid> UserIdsWithConsent() => _behind.UserIdsWithConsent();

        public DateTime? OldestPlayStartedUtc() => _behind.OldestPlayStartedUtc();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone)
            => _behind.YearsWithPlaysFor(userId, zone);

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => _behind.CountPlaysStartedBefore(cutoffUtc);

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysStartedBefore(cutoffUtc, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, fromUtc, toUtc, deletionClass, limit);

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => _behind.DeletionsRecorded(limit);

        public ConsentRecord? ConsentFor(Guid userId) => _behind.ConsentFor(userId);

        public void RecordConsent(ConsentRecord consent) => _behind.RecordConsent(consent);

        public void ForgetConsentFor(Guid userId) => _behind.ForgetConsentFor(userId);

        public void ReclaimFreedSpace() => _behind.ReclaimFreedSpace();

        public void Dispose() => _behind.Dispose();
    }
}
