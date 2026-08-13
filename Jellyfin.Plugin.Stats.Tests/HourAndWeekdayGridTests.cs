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
            Berlin);

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

        var grid = HourAndWeekdayGrid.Over([before, after], Berlin);

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

        var berlin = HourAndWeekdayGrid.Over([play], Berlin);
        var utc = HourAndWeekdayGrid.Over([play], TimeZoneInfo.Utc);

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
        Assert.Equal(Berlin.Id, HourAndWeekdayGrid.Over([], Berlin).Zone);
        Assert.Equal(TimeZoneInfo.Utc.Id, HourAndWeekdayGrid.Over([], TimeZoneInfo.Utc).Zone);
    }

    /// <summary>
    /// Every hour of the week is in the answer, in the order the drawing lays
    /// them out. An hour left out because nothing was played in it is a hole,
    /// and a hole and a quiet hour are different facts.
    /// </summary>
    [Fact]
    public void EveryHourOfTheWeekIsInTheAnswerEvenWithNoPlaysAtAll()
    {
        var grid = HourAndWeekdayGrid.Over([], Berlin);

        Assert.Equal(168, grid.Cells.Count);
        Assert.Equal(
            Enumerable.Range(0, 168).Select(index => (index / 24, index % 24)),
            grid.Cells.Select(cell => (cell.Weekday, cell.Hour)));
        Assert.All(grid.Cells, cell => Assert.Equal(0L, cell.Plays));
        Assert.All(grid.Cells, cell => Assert.Equal(0d, cell.WatchedMinutes));
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
        var grid = HourAndWeekdayGrid.Over([opened, watched], Berlin);

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

        var refused = Assert.Throws<ArgumentException>(() => HourAndWeekdayGrid.Over([play], Berlin));

        Assert.Contains(kind.ToString(), refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Neither argument has a sensible absence. A missing sequence is not an
    /// empty week and a missing zone is not UTC.
    /// </summary>
    [Fact]
    public void AMissingSequenceOrZoneIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => HourAndWeekdayGrid.Over(null!, Berlin));
        Assert.Throws<ArgumentNullException>(() => HourAndWeekdayGrid.Over([], null!));
    }

    private static long PlaysIn(HourAndWeekdayGrid grid, int weekday, int hour)
        => CellAt(grid, weekday, hour).Plays;

    private static double MinutesIn(HourAndWeekdayGrid grid, int weekday, int hour)
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
