// Reading plays into the hours of the week they started in.
//
// The failure these are written against is the one issue #58 names: a grid of
// hours whose midnight belongs to nobody in particular. It arrives two ways. A
// report can read the hours in whatever zone the machine happens to be in, and
// it can read them against a fixed offset taken once, which is right for half
// the year. The zone case is refused by no-ambient-clock in the greppable
// invariants and by the zone legs of the test workflow; the offset case is what
// the summer change tests below are for, and they are written so that the
// answer a fixed offset would give is the answer they refuse.
//
// Every row here is built in memory. No clock, no store and no file.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class HourAndWeekdayGridTests
{
    /// <summary>
    /// A zone with a summer change, well away from UTC in one direction, and
    /// the one the plan's own examples are written in.
    /// </summary>
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// A range wide enough that every hour of the week falls inside it, and
    /// wide enough to hold every play the cases below build. The cases about
    /// which hour a play lands in are then cases about that alone: a range that
    /// left some of the week out would make each of them pass or fail for two
    /// reasons at once.
    /// </summary>
    private static readonly DateTime EveryHourFromUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The other end of it.
    /// </summary>
    private static readonly DateTime EveryHourUntilUtc = new(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The four fields a cell may carry. Written out here rather than read off
    /// the type, because a list taken from the thing under test agrees with it
    /// however wrong both are.
    /// </summary>
    private static readonly string[] EveryFieldACellMayCarry =
    [
        "Hour",
        "Plays",
        "WatchedMinutes",
        "Weekday"
    ];

    /// <summary>
    /// The first half of the second condition of issue #58. A play at half past
    /// eleven at night in UTC is not at half past eleven for a reader in
    /// Berlin, and the hour it is drawn in is the reader's rather than the
    /// row's.
    /// </summary>
    [Fact]
    public void APlayLateAtNightIsCountedInTheHourTheZoneSays()
    {
        // A Wednesday in January, so the zone is on standard time and one hour
        // ahead of UTC. Half past eleven at night in UTC is half past midnight
        // on the Thursday in Berlin.
        var grid = HourAndWeekdayGrid.Over(
            [APlayStartedAt(new DateTime(2026, 1, 14, 23, 30, 0, DateTimeKind.Utc))],
            Berlin,
            EveryHourFromUtc,
            EveryHourUntilUtc);

        Assert.Equal(1L, PlaysIn(grid, weekday: 3, hour: 0));
        Assert.Equal(0L, PlaysIn(grid, weekday: 2, hour: 23));
    }

    /// <summary>
    /// The second half, and the reason the zone is converted through rather
    /// than subtracted. Two plays a day apart at the same moment of the UTC
    /// clock, with the zone's summer change between them, are an hour apart in
    /// the zone. A report holding one offset for the whole range puts them both
    /// in the same hour, which is the answer this refuses.
    /// </summary>
    [Fact]
    public void TheSummerChangeMovesAPlayIntoTheNextHourAndAFixedOffsetWouldNot()
    {
        // Berlin moves its clocks forward in the early hours of Sunday 29 March
        // 2026. The first play is before that and is read at UTC plus one, the
        // second is after it and is read at UTC plus two.
        var before = APlayStartedAt(new DateTime(2026, 3, 28, 23, 30, 0, DateTimeKind.Utc));
        var after = APlayStartedAt(new DateTime(2026, 3, 29, 23, 30, 0, DateTimeKind.Utc));

        var grid = HourAndWeekdayGrid.Over([before, after], Berlin, EveryHourFromUtc, EveryHourUntilUtc);

        // Sunday half past midnight, and Monday half past one.
        Assert.Equal(1L, PlaysIn(grid, weekday: 6, hour: 0));
        Assert.Equal(1L, PlaysIn(grid, weekday: 0, hour: 1));

        // What the offset that was right for the first play would have made of
        // the second. Asserting the absence is the point: the two lines above
        // pass under either reading of the first play, and only this one
        // separates the zone from a subtraction.
        Assert.Equal(0L, PlaysIn(grid, weekday: 0, hour: 0));
    }

    /// <summary>
    /// The same rows read in two zones are two different grids. Without this a
    /// grid that ignored its argument would pass every assertion above that
    /// happens to be written in one zone.
    /// </summary>
    [Fact]
    public void TheSameRowsReadInAnotherZoneLandInAnotherHour()
    {
        var play = APlayStartedAt(new DateTime(2026, 1, 14, 23, 30, 0, DateTimeKind.Utc));

        var berlin = HourAndWeekdayGrid.Over([play], Berlin, EveryHourFromUtc, EveryHourUntilUtc);
        var utc = HourAndWeekdayGrid.Over([play], TimeZoneInfo.Utc, EveryHourFromUtc, EveryHourUntilUtc);

        Assert.Equal(1L, PlaysIn(berlin, weekday: 3, hour: 0));
        Assert.Equal(1L, PlaysIn(utc, weekday: 2, hour: 23));
        Assert.Equal(0L, PlaysIn(berlin, weekday: 2, hour: 23));
        Assert.Equal(0L, PlaysIn(utc, weekday: 3, hour: 0));
    }

    /// <summary>
    /// The first condition of issue #58, held where it cannot be forgotten. The
    /// zone comes back with the figures, so a view drawing this grid has the
    /// zone in its hand and does not have to be told it a second time.
    /// </summary>
    [Fact]
    public void TheGridSaysWhichZoneItWasReadIn()
    {
        Assert.Equal(Berlin.Id, HourAndWeekdayGrid.Over([], Berlin, EveryHourFromUtc, EveryHourUntilUtc).Zone);
        Assert.Equal(
            TimeZoneInfo.Utc.Id,
            HourAndWeekdayGrid.Over([], TimeZoneInfo.Utc, EveryHourFromUtc, EveryHourUntilUtc).Zone);
    }

    /// <summary>
    /// Every hour of the week is in the answer, in the order the drawing lays
    /// them out. An hour left out of the answer because nothing was played in
    /// it is a hole in the grid, and a grid with holes in it is a shape a
    /// drawing has to guess at.
    /// </summary>
    [Fact]
    public void EveryHourOfTheWeekIsInTheAnswerEvenWithNoPlaysAtAll()
    {
        var grid = HourAndWeekdayGrid.Over([], Berlin, EveryHourFromUtc, EveryHourUntilUtc);

        Assert.Equal(168, grid.Cells.Count);
        Assert.Equal(
            Enumerable.Range(0, 168).Select(index => (index / 24, index % 24)),
            grid.Cells.Select(cell => (cell.Weekday, cell.Hour)));
        Assert.All(grid.Cells, cell => Assert.Equal(0L, cell.Plays));
        Assert.All(grid.Cells, cell => Assert.Equal(0d, cell.WatchedMinutes));
    }

    /// <summary>
    /// The third condition of issue #64, at the place the figure is made rather
    /// than at the place it is drawn. An hour the range never reached is not an
    /// hour nobody watched anything in, and the reader of a picture cannot tell
    /// the two apart once both are drawn as nought. The quiet hour is asserted
    /// beside the absent one on purpose: an answer that called every hour
    /// absent would pass the second assertion alone and would say nothing.
    /// </summary>
    [Fact]
    public void AnHourTheRangeNeverReachedIsAbsentAndAQuietHourInsideItIsNought()
    {
        // One Wednesday, read in UTC so the range and the hours are the same
        // clock and the case is about coverage alone.
        var wednesday = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);

        var grid = HourAndWeekdayGrid.Over(
            [APlayStartedAt(wednesday.AddHours(20).AddMinutes(5))],
            TimeZoneInfo.Utc,
            wednesday,
            wednesday.AddDays(1));

        Assert.Equal(1L, PlaysIn(grid, weekday: 2, hour: 20));

        // Inside the range and quiet.
        Assert.Equal(0L, PlaysIn(grid, weekday: 2, hour: 5));
        Assert.Equal(0d, MinutesIn(grid, weekday: 2, hour: 5));

        // The same hour of a day the range never reached.
        Assert.Null(PlaysIn(grid, weekday: 0, hour: 5));
        Assert.Null(MinutesIn(grid, weekday: 0, hour: 5));

        // One day of the seven answers, and the other six say they cannot.
        Assert.Equal(24, grid.Cells.Count(cell => cell.Plays.HasValue));
        Assert.Equal(144, grid.Cells.Count(cell => cell.Plays is null));
    }

    /// <summary>
    /// Which hours a range reaches is read through the zone, the same way a
    /// play is. A day of UTC is not a day of anybody else's clock: read in
    /// Berlin in January it starts at one in the morning and ends at one the
    /// next, so midnight on the Wednesday is an hour the range does not hold
    /// and midnight on the Thursday is one it does. Subtracting hours off the
    /// range instead would put both ends an hour out and call an hour quiet
    /// that nothing could have been played in.
    /// </summary>
    [Fact]
    public void WhichHoursTheRangeReachedIsReadInTheZoneTheGridIs()
    {
        var wednesday = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);

        var berlin = HourAndWeekdayGrid.Over([], Berlin, wednesday, wednesday.AddDays(1));
        var utc = HourAndWeekdayGrid.Over([], TimeZoneInfo.Utc, wednesday, wednesday.AddDays(1));

        // Berlin is an hour ahead in January, so the range covers Wednesday
        // from one in the morning and reaches into the Thursday.
        Assert.Null(PlaysIn(berlin, weekday: 2, hour: 0));
        Assert.Equal(0L, PlaysIn(berlin, weekday: 2, hour: 1));
        Assert.Equal(0L, PlaysIn(berlin, weekday: 3, hour: 0));
        Assert.Null(PlaysIn(berlin, weekday: 3, hour: 1));

        // The same range in UTC covers the Wednesday and nothing of the
        // Thursday. Both readings cannot be right, and only one of them is
        // about the zone the figures were counted in.
        Assert.Equal(0L, PlaysIn(utc, weekday: 2, hour: 0));
        Assert.Null(PlaysIn(utc, weekday: 3, hour: 0));
    }

    /// <summary>
    /// A range of a whole week reaches every hour of it, so nothing is absent
    /// and the answer is the one this fold gave before it was told the range.
    /// Without this the repair could be a fold that calls everything absent,
    /// which is a week no reader learns anything from.
    /// </summary>
    [Fact]
    public void ARangeOfAWholeWeekLeavesNoHourAbsent()
    {
        var monday = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);

        var grid = HourAndWeekdayGrid.Over([], TimeZoneInfo.Utc, monday, monday.AddDays(7));

        Assert.All(grid.Cells, cell => Assert.Equal(0L, cell.Plays));
        Assert.All(grid.Cells, cell => Assert.Equal(0d, cell.WatchedMinutes));
    }

    /// <summary>
    /// A range shorter than an hour still reaches both hours it lies across.
    /// The walk steps by the hour, so the only thing that reaches the hour the
    /// range ends in is the last moment of it, and a walk that stopped at its
    /// last whole step would call that hour absent while holding plays from it.
    /// </summary>
    [Fact]
    public void ARangeShorterThanAnHourReachesBothHoursItLiesAcross()
    {
        // Ten to eleven at night until twenty past, on a Wednesday, read in UTC.
        var from = new DateTime(2026, 1, 14, 22, 50, 0, DateTimeKind.Utc);

        var grid = HourAndWeekdayGrid.Over([], TimeZoneInfo.Utc, from, from.AddMinutes(30));

        Assert.Equal(0L, PlaysIn(grid, weekday: 2, hour: 22));
        Assert.Equal(0L, PlaysIn(grid, weekday: 2, hour: 23));
        Assert.Equal(2, grid.Cells.Count(cell => cell.Plays.HasValue));
    }

    /// <summary>
    /// The hour a zone skips when it moves its clocks forward did not happen,
    /// so nothing could have been played in it, and calling it quiet is the
    /// same mistake as calling an unreached hour quiet. It falls out of reading
    /// the range through the zone rather than being written down: an hour that
    /// never occurs is never reached. The hour after it is asserted beside it,
    /// because an answer that lost the whole morning would pass the first line
    /// on its own.
    /// </summary>
    [Fact]
    public void AnHourTheZoneSkipsIsNotCalledQuiet()
    {
        // Berlin moves its clocks forward at two in the morning on Sunday 29
        // March 2026, which is one o'clock in UTC. There is no half past two
        // that day.
        var sunday = new DateTime(2026, 3, 29, 0, 0, 0, DateTimeKind.Utc);

        var grid = HourAndWeekdayGrid.Over([], Berlin, sunday, sunday.AddDays(1));

        Assert.Equal(0L, PlaysIn(grid, weekday: 6, hour: 1));
        Assert.Null(PlaysIn(grid, weekday: 6, hour: 2));
        Assert.Equal(0L, PlaysIn(grid, weekday: 6, hour: 3));
    }

    /// <summary>
    /// The two figures are counted separately and neither is derived from the
    /// other. A play somebody opened and left is a play with no watched time,
    /// and an hour reporting no watched time as no plays would hide exactly the
    /// hour an administrator is looking for.
    /// </summary>
    [Fact]
    public void PlaysAndWatchedMinutesAreCountedSeparately()
    {
        var opened = APlayStartedAt(new DateTime(2026, 1, 14, 20, 5, 0, DateTimeKind.Utc)) with
        {
            WatchedDuration = TimeSpan.Zero
        };
        var watched = APlayStartedAt(new DateTime(2026, 1, 14, 20, 55, 0, DateTimeKind.Utc)) with
        {
            WatchedDuration = TimeSpan.FromMinutes(38)
        };

        // Nine in the evening in Berlin, which is eight in UTC.
        var grid = HourAndWeekdayGrid.Over([opened, watched], Berlin, EveryHourFromUtc, EveryHourUntilUtc);

        Assert.Equal(2L, PlaysIn(grid, weekday: 2, hour: 21));
        Assert.Equal(38d, MinutesIn(grid, weekday: 2, hour: 21));
    }

    /// <summary>
    /// The third condition of issue #58 on the side the figures come from. A
    /// view can only name a user if something hands it one, and this is the
    /// shape that would. The field list is compared rather than eyeballed, so a
    /// column added to a cell fails here rather than appearing on a dashboard.
    /// </summary>
    [Fact]
    public void ACellCarriesTheFourFiguresAndNothingThatNamesAnybody()
    {
        var carried = typeof(WeekCell)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(EveryFieldACellMayCarry, carried);
    }

    /// <summary>
    /// A moment that is not in UTC is a moment whose meaning depends on the
    /// machine that built it. The store refuses to write one, so a row in that
    /// shape is a reader that assembled it wrongly, and counting it would put
    /// the play in whatever hour that machine made of it.
    /// <para>
    /// Both of the other two kinds, because they fail differently. A local
    /// moment is one the framework would convert a second time; an unspecified
    /// one is one nothing can convert at all and which would be taken for UTC
    /// in silence. The refusal is written in this plugin rather than left to the
    /// conversion for the reason the second line here shows: on a machine that
    /// happens to be at UTC the framework accepts a local moment, so a check
    /// resting on it would pass on the runner and fail on a server.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ARowWhoseMomentIsNotInUtcIsRefusedRatherThanCounted(DateTimeKind kind)
    {
        var play = APlayStartedAt(new DateTime(2026, 1, 14, 23, 30, 0, kind));

        var refused = Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([play], Berlin, EveryHourFromUtc, EveryHourUntilUtc));

        Assert.Contains(kind.ToString(), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same refusal one step further out. A bound of the range that does
    /// not say it is in UTC is a bound whose meaning is the offset of whichever
    /// machine built it, and what that changes is which hours the answer calls
    /// absent, so the mistake arrives as a week that reads perfectly well and
    /// is an hour out at both ends. Both bounds, because a check written for
    /// one of them passes the other.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ABoundOfTheRangeThatIsNotInUtcIsRefused(DateTimeKind kind)
    {
        var wrong = new DateTime(2026, 1, 14, 0, 0, 0, kind);
        var right = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var first = Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([], Berlin, wrong, right));
        var second = Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([], Berlin, EveryHourFromUtc, new DateTime(2027, 1, 1, 0, 0, 0, kind)));

        Assert.Contains(kind.ToString(), first.Message, StringComparison.Ordinal);
        Assert.Contains(kind.ToString(), second.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A range holding no moment at all would come back as a week in which
    /// every hour is absent, which is a shape a reader can read and a caller
    /// cannot have meant. It is refused rather than answered, because the
    /// answer would be indistinguishable from a store that has lost everything.
    /// </summary>
    [Fact]
    public void ARangeThatEndsWhereItStartsOrEarlierIsRefused()
    {
        var moment = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() => HourAndWeekdayGrid.Over([], Berlin, moment, moment));
        Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([], Berlin, moment, moment.AddSeconds(-1)));
    }

    /// <summary>
    /// The rows and the range are one fact rather than two, and a caller that
    /// chose them separately is caught here. A play outside the range would be
    /// counted into an hour the same answer goes on to call absent, and a cell
    /// that is both is a cell no drawing and no reader can make sense of.
    /// </summary>
    [Fact]
    public void APlayOutsideTheRangeIsRefusedRatherThanCounted()
    {
        var wednesday = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);
        var theDayBefore = APlayStartedAt(wednesday.AddHours(-3));
        var theMomentItEnds = APlayStartedAt(wednesday.AddDays(1));

        Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([theDayBefore], Berlin, wednesday, wednesday.AddDays(1)));

        // The last moment of the range is outside it, the same way the hour
        // after the last hour is. A bound that is inclusive at one end and
        // exclusive at the other is the off-by-one nobody finds afterwards.
        Assert.Throws<ArgumentException>(
            () => HourAndWeekdayGrid.Over([theMomentItEnds], Berlin, wednesday, wednesday.AddDays(1)));
    }

    /// <summary>
    /// Neither argument has a sensible absence. A missing sequence is not an
    /// empty week and a missing zone is not UTC.
    /// </summary>
    [Fact]
    public void AMissingSequenceOrZoneIsRefused()
    {
        Assert.Throws<ArgumentNullException>(
            () => HourAndWeekdayGrid.Over(null!, Berlin, EveryHourFromUtc, EveryHourUntilUtc));
        Assert.Throws<ArgumentNullException>(
            () => HourAndWeekdayGrid.Over([], null!, EveryHourFromUtc, EveryHourUntilUtc));
    }

    private static long? PlaysIn(HourAndWeekdayGrid grid, int weekday, int hour)
        => CellAt(grid, weekday, hour).Plays;

    private static double? MinutesIn(HourAndWeekdayGrid grid, int weekday, int hour)
        => CellAt(grid, weekday, hour).WatchedMinutes;

    private static WeekCell CellAt(HourAndWeekdayGrid grid, int weekday, int hour)
        => grid.Cells.Single(cell => cell.Weekday == weekday && cell.Hour == hour);

    private static PlayRecord APlayStartedAt(DateTime startedUtc) => new()
    {
        SchemaVersion = 1,
        UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Episode",
        ParentId = null,
        ItemName = "An episode",
        ItemRuntime = TimeSpan.FromMinutes(42),
        StartedUtc = startedUtc,
        EndedUtc = startedUtc.AddMinutes(41),
        WatchedDuration = TimeSpan.FromMinutes(38),
        ReachedTheEnd = true,
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
