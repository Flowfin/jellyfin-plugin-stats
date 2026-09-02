// What a wrap-up says about the part of its year the store could still answer
// for, and the two failures that statement exists against.
//
// The first is a partial year presented as a whole one. Play rows are deleted
// after ninety days by default, so the ordinary wrap-up on an ordinary server
// is folded over a quarter of the year on its heading, and nothing in the
// figures gives that away: they are correct arithmetic over real plays and they
// add up. Only a sentence beside them can say which days they are about.
//
// The second is a figure scaled up to the year it claims. Four months of plays
// multiplied by three is a number about rows that were deleted, produced by the
// thing whose whole job is to report what was kept, and it is indistinguishable
// on a page from a number that was counted.
//
// The last case here drives a real store and the real retention sweep over a
// temporary directory: rows are written across a year, the sweep is given a
// cutoff four months from its end, and the wrap-up folded afterwards is asked
// what it covers. Nothing needs a server, a socket or a clock; every moment is
// a value written into the case.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class YearCoverageTests : IDisposable
{
    private static readonly Guid Mine = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Theirs = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// The year every case here is about. It is not a leap year, so the whole
    /// year is three hundred and sixty-five days and a slip of one is visible.
    /// </summary>
    private const int Year = 2025;

    /// <summary>
    /// An aggregate window nothing here falls outside of. These cases are
    /// about the play rows, and a sweep that took the days they were folded
    /// into as well would be proving two windows at once.
    /// </summary>
    private static readonly DateTime KeepEveryRollup = DateTime.UnixEpoch;

    private readonly string _root;

    public YearCoverageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #69, at the fold: a store holding four
    /// months of a year produces a wrap-up that says it covers four months.
    /// The window is read from the store's oldest row and stated as two days
    /// and a count, so a reader is told what the figures are about rather than
    /// having to work it out from them.
    /// </summary>
    [Fact]
    public void AStoreHoldingFourMonthsSaysItCoversFourMonths()
    {
        var plays = new[]
        {
            APlayStartedAt(new DateTime(Year, 9, 1, 12, 0, 0, DateTimeKind.Utc)),
            APlayStartedAt(new DateTime(Year, 12, 24, 20, 0, 0, DateTimeKind.Utc))
        };

        var review = YearInReview.Over(
            plays,
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year, 9, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(review.Coverage.WholeYear);
        Assert.Equal(new DateOnly(Year, 9, 1), review.Coverage.FirstDayCovered);
        Assert.Equal(new DateOnly(Year, 12, 31), review.Coverage.LastDayCovered);

        // September has thirty days, October thirty-one, November thirty and
        // December thirty-one. Written out rather than as a subtraction, so the
        // number this case is named after is in the case.
        Assert.Equal(30 + 31 + 30 + 31, review.Coverage.DaysCovered);
    }

    /// <summary>
    /// A store whose oldest row is older than the year has lost none of it, and
    /// says so. This is the case a reader has to be able to tell from the one
    /// above, and it is the reason the window is stated on every wrap-up rather
    /// than only on a short one: an absent statement would leave both of them
    /// looking the same.
    /// </summary>
    [Fact]
    public void AStoreOlderThanTheYearCoversAllOfIt()
    {
        var review = YearInReview.Over(
            new[] { APlayStartedAt(new DateTime(Year, 6, 1, 12, 0, 0, DateTimeKind.Utc)) },
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year - 3, 4, 5, 6, 0, 0, DateTimeKind.Utc));

        Assert.True(review.Coverage.WholeYear);
        Assert.Equal(new DateOnly(Year, 1, 1), review.Coverage.FirstDayCovered);
        Assert.Equal(365, review.Coverage.DaysCovered);
    }

    /// <summary>
    /// A store with nothing in it, and a store whose rows all started after the
    /// year ended, both cover none of it. The second is the one worth having a
    /// case for: a window running from a day in a later year to the last day of
    /// this one is a window that finishes before it begins, and its length is a
    /// negative number that reads as a year covered backwards.
    /// </summary>
    [Fact]
    public void AStoreThatCanAnswerForNoneOfTheYearSaysSo()
    {
        var empty = YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: null);

        Assert.False(empty.Coverage.WholeYear);
        Assert.Null(empty.Coverage.FirstDayCovered);
        Assert.Equal(0, empty.Coverage.DaysCovered);

        var later = YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year + 1, 2, 3, 4, 0, 0, DateTimeKind.Utc));

        Assert.Null(later.Coverage.FirstDayCovered);
        Assert.Equal(0, later.Coverage.DaysCovered);
    }

    /// <summary>
    /// The window is read in the zone the wrap-up was asked for and not in the
    /// machine's. The moment chosen is half past ten at night on the last day of
    /// August in UTC, which is already the first of September in Berlin, so a
    /// reading that ignored the zone would put the edge of the window on the
    /// wrong side of a month boundary and hand back one extra day.
    /// </summary>
    [Fact]
    public void TheEdgeOfTheWindowIsReadInTheZoneTheYearIs()
    {
        var oldest = new DateTime(Year, 8, 31, 22, 30, 0, DateTimeKind.Utc);

        var berlin = YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            Year,
            Berlin,
            10,
            oldestPlayStartedUtc: oldest);

        var utc = YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: oldest);

        Assert.Equal(new DateOnly(Year, 9, 1), berlin.Coverage.FirstDayCovered);
        Assert.Equal(new DateOnly(Year, 8, 31), utc.Coverage.FirstDayCovered);
        Assert.Equal(122, berlin.Coverage.DaysCovered);
        Assert.Equal(123, utc.Coverage.DaysCovered);
    }

    /// <summary>
    /// A start that does not say it is in UTC is refused rather than read in
    /// whatever zone the reader sits in. It is the same refusal the fold already
    /// makes at a row's own start, at the one other moment that decides a
    /// boundary: read wrongly it produces a real window over the wrong days, and
    /// nothing downstream can tell that from the right answer.
    /// </summary>
    [Fact]
    public void AnOldestStartThatIsNotInUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(() => YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year, 9, 1, 12, 0, 0, DateTimeKind.Local)));
    }

    /// <summary>
    /// The earliest row the wrap-up had and the first day it could have had one
    /// are two different statements, and the case that separates them is the
    /// reason the window is not read off this person's own plays. Somebody whose
    /// first play of the year was in September on a store that goes back to
    /// January watched nothing until September; reading the window off their
    /// rows would report that as a retention cut. The other account's January
    /// row is what makes the store's reading the earlier of the two.
    /// </summary>
    [Fact]
    public void TheEarliestRowIsNotTheEdgeOfTheWindow()
    {
        var plays = new[]
        {
            APlayStartedAt(new DateTime(Year, 1, 1, 9, 0, 0, DateTimeKind.Utc), Theirs),
            APlayStartedAt(new DateTime(Year, 9, 20, 9, 0, 0, DateTimeKind.Utc)),
            APlayStartedAt(new DateTime(Year, 11, 2, 9, 0, 0, DateTimeKind.Utc))
        };

        var review = YearInReview.Over(
            plays,
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year, 1, 1, 9, 0, 0, DateTimeKind.Utc));

        Assert.True(review.Coverage.WholeYear);
        Assert.Equal(new DateOnly(Year, 1, 1), review.Coverage.FirstDayCovered);
        Assert.Equal(new DateOnly(Year, 9, 20), review.Coverage.EarliestPlay);
    }

    /// <summary>
    /// A person with no play in the year has no earliest row, and the window is
    /// still stated. A wrap-up that dropped the window along with the figures
    /// would leave the reader of an empty year unable to tell a quiet year from
    /// one whose rows are gone, which is the distinction the empty answer exists
    /// for in the first place.
    /// </summary>
    [Fact]
    public void AYearWithNoPlayStillSaysWhatItCovers()
    {
        var review = YearInReview.Over(
            new[] { APlayStartedAt(new DateTime(Year, 10, 5, 9, 0, 0, DateTimeKind.Utc), Theirs) },
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            oldestPlayStartedUtc: new DateTime(Year, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.False(review.AnythingRecorded);
        Assert.Null(review.Coverage.EarliestPlay);
        Assert.Equal(new DateOnly(Year, 9, 1), review.Coverage.FirstDayCovered);
        Assert.Equal(122, review.Coverage.DaysCovered);
    }

    /// <summary>
    /// The third condition of issue #69, driven end to end: a retention shorter
    /// than the year, the real sweep run over a real store, and the wrap-up
    /// folded afterwards asked what it covers.
    /// </summary>
    /// <remarks>
    /// The rows are one a month, so the cutoff falls exactly on a row and the
    /// four months the answer names are four months and not an arithmetic
    /// accident. The second half of the case is the second condition: every
    /// figure is compared against the rows that survived, so a fold that scaled
    /// anything up to the year on the heading disagrees with the count taken
    /// directly.
    /// </remarks>
    [Fact]
    public void ASweepShorterThanTheYearLeavesAWrapUpThatSaysWhatItCovers()
    {
        var months = Enumerable
            .Range(1, 12)
            .Select(month => new DateTime(Year, month, 1, 12, 0, 0, DateTimeKind.Utc))
            .ToList();

        using (var store = new SqlitePlayStore(_root))
        {
            foreach (var month in months)
            {
                store.Add(APlayStartedAt(month));
            }
        }

        var cutoff = new DateTime(Year, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var deleted = new RetentionSweep(() => new SqlitePlayStore(_root), RetentionSweep.DefaultBite)
            .Run(cutoff, KeepEveryRollup, new IgnoredProgress(), CancellationToken.None);

        Assert.Equal(8, deleted.Plays);

        using var after = new SqlitePlayStore(_root);
        var survivors = after.PlaysFor(Mine).ToList();

        var review = YearInReview.Over(
            survivors,
            Mine,
            Year,
            TimeZoneInfo.Utc,
            10,
            after.OldestPlayStartedUtc());

        Assert.False(review.Coverage.WholeYear);
        Assert.Equal(new DateOnly(Year, 9, 1), review.Coverage.FirstDayCovered);
        Assert.Equal(new DateOnly(Year, 12, 31), review.Coverage.LastDayCovered);
        Assert.Equal(30 + 31 + 30 + 31, review.Coverage.DaysCovered);
        Assert.Equal(new DateOnly(Year, 9, 1), review.Coverage.EarliestPlay);

        // Nothing is scaled up to the year on the heading. Four rows survived,
        // so the wrap-up reports four plays and the watched time those four
        // recorded, and not twelve of either.
        Assert.Equal(4, survivors.Count);
        Assert.Equal(4L, review.Plays);
        Assert.Equal(
            survivors.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
            review.Watched);
        Assert.Equal(4L, review.Delivery!.Plays);
        Assert.Equal(new DateOnly(Year, 9, 1), review.BusiestDay!.Day);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static PlayRecord APlayStartedAt(DateTime startedUtc)
        => APlayStartedAt(startedUtc, Mine);

    private static PlayRecord APlayStartedAt(DateTime startedUtc, Guid userId)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
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
                Reasons = []
            }
        };
    }

    /// <summary>
    /// A progress reporter for the case that is not about progress.
    /// </summary>
    private sealed class IgnoredProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }
}
