// Plays and watched time per day, and the four things the series must never do.
//
// It must not lose a play. Every play falls on exactly one day, so the rows add
// up to the plays and to the watched time they came from, and the failure that
// is written against is a chart whose bars total less than the count printed
// beside them.
//
// It must not answer in the machine's zone. Which day a play falls on depends on
// whose midnight is meant, so every fold here names the zone and the answer is
// asked to name it back, and a row whose start does not say it is in UTC is
// refused rather than read against whatever zone the reader happens to sit in.
//
// It must not invent a day. A day with no plays is not a row of noughts, because
// this fold cannot tell an empty day from one the retention sweep emptied or one
// the range never covered.
//
// And it must not count time nobody watched. A session left open is not a film
// watched, which is the distinction the stored row already keeps.
//
// Every row is built in memory and no clock, zone setting or store is touched.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class DailyUsageTests
{
    private static readonly DateTime Noon = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    /// <summary>
    /// The property the first condition of issue #57 rests on, over sequences
    /// nobody chose one at a time: every play is under exactly one day, so the
    /// rows add up to the plays and to the watched time. Each row is checked
    /// against a count taken the other way round, walking the day and counting
    /// the plays that fall on it, so an accumulator that lost or double counted
    /// anything disagrees with it. The generator is seeded, so a failure is the
    /// same failure on the next run and on the runner.
    /// </summary>
    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/Berlin")]
    [InlineData("Pacific/Auckland")]
    public void EveryPlayIsUnderExactlyOneDayAndTheDaysAscend(string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var generator = new Random(20260813);

        for (var sweep = 0; sweep < 300; sweep++)
        {
            var length = generator.Next(0, 90);
            var plays = new List<PlayRecord>(length);
            for (var i = 0; i < length; i++)
            {
                plays.Add(APlay(
                    startedUtc: Noon.AddMinutes(generator.Next(-20000, 20000)),
                    watched: TimeSpan.FromSeconds(generator.Next(0, 7200)),
                    method: (PlayMethod)generator.Next(0, 4)));
            }

            var usage = DailyUsage.Over(plays, zone);

            Assert.Equal((long)length, usage.Plays);
            Assert.Equal(usage.Plays, usage.Rows.Sum(row => row.Delivery.Plays));
            Assert.Equal(usage.Watched, usage.Rows.Aggregate(TimeSpan.Zero, (total, row) => total + row.Watched));
            Assert.Equal(usage.Rows.Select(row => row.Day).Distinct().Count(), usage.Rows.Count);

            foreach (var row in usage.Rows)
            {
                var onThatDay = plays.Where(play => DayOf(play, zone) == row.Day).ToList();

                Assert.Equal(onThatDay.Count, row.Delivery.Plays);
                Assert.Equal(
                    onThatDay.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
                    row.Watched);
                Assert.InRange(row.Delivery.Plays, 1, usage.Plays);
            }

            for (var i = 1; i < usage.Rows.Count; i++)
            {
                Assert.True(
                    usage.Rows[i - 1].Day < usage.Rows[i].Day,
                    "Days ascend, and " + usage.Rows[i - 1].Day + " came before " + usage.Rows[i].Day + ".");
            }
        }
    }

    /// <summary>
    /// Which day a play falls on is read in the zone handed in and not in UTC.
    /// The failure this is written against is the one that looks like nothing at
    /// all: an evening play appearing on the day before for every reader east of
    /// Greenwich, which shows up as a chart whose weekends are in the wrong
    /// place rather than as an error.
    /// </summary>
    [Fact]
    public void APlayCountsOnTheDayItStartedInTheZoneItWasReadIn()
    {
        var lateEvening = new DateTime(2026, 3, 14, 23, 30, 0, DateTimeKind.Utc);

        var inUtc = DailyUsage.Over(new[] { APlay(startedUtc: lateEvening) }, TimeZoneInfo.Utc);
        var inBerlin = DailyUsage.Over(new[] { APlay(startedUtc: lateEvening) }, Berlin);

        Assert.Equal(new DateOnly(2026, 3, 14), Assert.Single(inUtc.Rows).Day);
        Assert.Equal(new DateOnly(2026, 3, 15), Assert.Single(inBerlin.Rows).Day);
    }

    /// <summary>
    /// A play that ran over midnight is one row on the day it started, carrying
    /// the whole of what was watched. Splitting it would mean deciding how much
    /// of the watching happened on each side, and the row says when the play
    /// began and how much of the item was watched, never when within the play
    /// the watching was.
    /// </summary>
    [Fact]
    public void APlayThatRanOverMidnightIsOneRowOnTheDayItStarted()
    {
        var usage = DailyUsage.Over(
            new[]
            {
                APlay(
                    startedUtc: new DateTime(2026, 3, 14, 23, 30, 0, DateTimeKind.Utc),
                    endedUtc: new DateTime(2026, 3, 15, 1, 10, 0, DateTimeKind.Utc),
                    watched: TimeSpan.FromMinutes(90))
            },
            TimeZoneInfo.Utc);

        var row = Assert.Single(usage.Rows);

        Assert.Equal(new DateOnly(2026, 3, 14), row.Day);
        Assert.Equal(TimeSpan.FromMinutes(90), row.Watched);
        Assert.Equal(1, row.Delivery.Plays);
    }

    /// <summary>
    /// Watched time is what the row recorded as watched and not how long its
    /// session was open. A film paused and left overnight would otherwise be
    /// eight hours of viewing, which is wrong in the direction that flatters the
    /// numbers and is the reason the stored row keeps the two apart.
    /// </summary>
    [Fact]
    public void WatchedTimeIsWhatWasWatchedAndNotHowLongTheSessionWasOpen()
    {
        var usage = DailyUsage.Over(
            new[]
            {
                APlay(
                    startedUtc: Noon,
                    endedUtc: Noon.AddHours(8),
                    watched: TimeSpan.FromMinutes(12))
            },
            TimeZoneInfo.Utc);

        Assert.Equal(TimeSpan.FromMinutes(12), Assert.Single(usage.Rows).Watched);
        Assert.Equal(TimeSpan.FromMinutes(12), usage.Watched);
    }

    /// <summary>
    /// A day nothing was played on is not a row of noughts. This fold is handed
    /// a sequence and not a range, so a gap here may be a quiet day, a day the
    /// retention sweep emptied, or a day the range never covered, and writing
    /// nought for all three destroys the difference before a page can draw it.
    /// </summary>
    [Fact]
    public void ADayWithNoPlaysIsNotARowOfNoughts()
    {
        var usage = DailyUsage.Over(
            new[]
            {
                APlay(startedUtc: Noon),
                APlay(startedUtc: Noon.AddDays(7))
            },
            TimeZoneInfo.Utc);

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 14), new DateOnly(2026, 3, 21) },
            usage.Rows.Select(row => row.Day).ToArray());
    }

    /// <summary>
    /// The days ascend however the plays arrived. A series drawn in the order a
    /// query happened to return its rows is a chart whose line doubles back on
    /// itself when an index changes, so the order is settled here.
    /// </summary>
    [Fact]
    public void TheDaysAscendHoweverThePlaysArrived()
    {
        var usage = DailyUsage.Over(
            new[]
            {
                APlay(startedUtc: Noon.AddDays(4)),
                APlay(startedUtc: Noon.AddDays(2)),
                APlay(startedUtc: Noon.AddDays(9)),
                APlay(startedUtc: Noon)
            },
            TimeZoneInfo.Utc);

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 3, 14),
                new DateOnly(2026, 3, 16),
                new DateOnly(2026, 3, 18),
                new DateOnly(2026, 3, 23)
            },
            usage.Rows.Select(row => row.Day).ToArray());
    }

    /// <summary>
    /// The answer names the zone its days were read in. The same two plays are
    /// one day or two depending on the zone, so a series of days without the
    /// zone beside it is not readable, and a page that states a zone it was not
    /// given is quoting a setting rather than describing the numbers it drew.
    /// </summary>
    [Fact]
    public void TheAnswerNamesTheZoneItsDaysWereReadIn()
    {
        var plays = new[]
        {
            APlay(startedUtc: new DateTime(2026, 3, 14, 1, 0, 0, DateTimeKind.Utc)),
            APlay(startedUtc: new DateTime(2026, 3, 14, 23, 0, 0, DateTimeKind.Utc))
        };

        var inUtc = DailyUsage.Over(plays, TimeZoneInfo.Utc);
        var inAuckland = DailyUsage.Over(plays, Auckland);

        Assert.Single(inUtc.Rows);
        Assert.Equal(2, inAuckland.Rows.Count);

        Assert.Equal(TimeZoneInfo.Utc.Id, inUtc.ZoneId);
        Assert.Equal(Auckland.Id, inAuckland.ZoneId);
    }

    /// <summary>
    /// The direct and transcoded split the issue asks to be visible in the same
    /// view, and it is the same four figures the whole range answers with rather
    /// than a second definition. A day's figures add up to that day's plays for
    /// the same reason the range's do.
    /// </summary>
    [Fact]
    public void EachDayCarriesTheDeliveryFiguresForItsOwnPlays()
    {
        var usage = DailyUsage.Over(
            new[]
            {
                APlay(startedUtc: Noon, method: PlayMethod.Transcode),
                APlay(startedUtc: Noon.AddHours(1), method: PlayMethod.DirectPlay),
                APlay(startedUtc: Noon.AddHours(2), method: PlayMethod.Unknown),
                APlay(startedUtc: Noon.AddDays(1), method: PlayMethod.DirectStream)
            },
            TimeZoneInfo.Utc);

        var first = Assert.Single(usage.Rows, row => row.Day == new DateOnly(2026, 3, 14));

        Assert.Equal(1, first.Delivery.Transcode);
        Assert.Equal(1, first.Delivery.DirectPlay);
        Assert.Equal(1, first.Delivery.Unknown);
        Assert.Equal(0, first.Delivery.DirectStream);
        Assert.Equal(3, first.Delivery.Plays);
    }

    /// <summary>
    /// The third condition of issue #57 asks that the view name no user,
    /// asserted against the answer rather than by reading the page. A row that
    /// could name a user is a row a page could draw one from, whatever the page
    /// chooses to show, so what is checked is the shape of the type: these three
    /// members and nothing else.
    /// </summary>
    [Fact]
    public void ARowHasNowhereToPutAUser()
    {
        Assert.Equal(
            new[] { "Day", "Delivery", "Watched" },
            typeof(DailyUsageRow)
                .GetProperties()
                .Select(property => property.Name)
                .Where(name => name != "EqualityContract")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// An empty sequence is an answer with no days in it, and it still names the
    /// zone it was read in. Nothing here divides, so there is no figure that has
    /// to invent a value for a range with no plays.
    /// </summary>
    [Fact]
    public void AnEmptySequenceIsNoDaysRatherThanADayOfNoughts()
    {
        var usage = DailyUsage.Over(Array.Empty<PlayRecord>(), Berlin);

        Assert.Empty(usage.Rows);
        Assert.Equal(0, usage.Plays);
        Assert.Equal(TimeSpan.Zero, usage.Watched);
        Assert.Equal(Berlin.Id, usage.ZoneId);
    }

    [Fact]
    public void AMissingSequenceIsRefusedRatherThanReadAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => DailyUsage.Over(null!, TimeZoneInfo.Utc));
    }

    [Fact]
    public void AMissingZoneIsRefusedRatherThanFilledInFromTheMachine()
    {
        Assert.Throws<ArgumentNullException>(() => DailyUsage.Over(Array.Empty<PlayRecord>(), null!));
    }

    /// <summary>
    /// A play whose start is not in UTC is refused rather than read in the
    /// machine's zone. It is the failure that leaves no trace: the day would be
    /// a real day, the rows would still add up, and the whole series would be
    /// off by the offset of whichever machine answered. The store refuses the
    /// same field at the other end for the same reason, where a timestamp that
    /// is not UTC is refused at the write rather than repaired at the read.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AStartThatIsNotUtcIsRefusedRatherThanReadInTheMachinesZone(DateTimeKind kind)
    {
        var plays = new[] { APlay(startedUtc: DateTime.SpecifyKind(Noon, kind)) };

        Assert.Throws<ArgumentException>(() => DailyUsage.Over(plays, Berlin));
    }

    /// <summary>
    /// The day a play falls on, derived here the way the test reads the issue
    /// rather than the way the fold is written, so the two agree by measurement
    /// and not by sharing a line of code.
    /// </summary>
    private static DateOnly DayOf(PlayRecord play, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(new DateTimeOffset(play.StartedUtc), zone).DateTime);

    private static PlayRecord APlay(
        DateTime? startedUtc = null,
        DateTime? endedUtc = null,
        TimeSpan? watched = null,
        PlayMethod method = PlayMethod.DirectPlay) => new()
        {
            SchemaVersion = 1,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = startedUtc ?? Noon,
            EndedUtc = endedUtc ?? (startedUtc ?? Noon).AddMinutes(41),
            WatchedDuration = watched ?? TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = method,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
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
