// One person's year, and the four things a wrap-up must never do.
//
// It must not tell somebody about a year that was not theirs. The fold is
// handed a sequence that carries everybody's rows, because that is what the
// reads in this tree hand back, and it does its own filtering; the failure
// written against here is a wrap-up that is correct arithmetic over the wrong
// person.
//
// It must not answer an empty year with noughts. A year the retention sweep
// emptied, a year before anything was recorded and a genuinely quiet year are
// three statements, and a dashboard of zeros is the same picture for all three.
//
// It must not disagree with the chart beside it. The days come from the same
// fold the daily chart is drawn from and the month is those days added up, so
// there is one answer to where a day ends rather than two.
//
// And it must not read a year in the machine's zone. A play at half past eleven
// on New Year's Eve is in one year for one reader and the next for another, so
// every fold here names the zone, and a row whose start does not say it is in
// UTC is refused rather than read against whatever zone the reader sits in.
//
// Every row is built in memory and no clock, zone setting or store is touched.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class YearInReviewTests
{
    private static readonly Guid Mine = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Theirs = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    /// <summary>
    /// The first condition of issue #66, over years nobody chose one at a time.
    /// Every figure the wrap-up answers with is computed a second time here, by
    /// walking the same generated rows the way the issue words each figure
    /// rather than the way the fold is written, so an accumulator that lost a
    /// play, counted one twice or ranked on the wrong field disagrees with it.
    /// The rows carry two users and three years, so the filtering is part of
    /// what every figure is checked against. The generator is seeded, so a
    /// failure is the same failure on the next run and on the runner.
    /// </summary>
    [Theory]
    [InlineData("UTC")]
    [InlineData("Europe/Berlin")]
    [InlineData("Pacific/Auckland")]
    public void EveryFigureAgreesWithTheSameCountTakenTheOtherWay(string zoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var generator = new Random(20260817);

        for (var sweep = 0; sweep < 200; sweep++)
        {
            var plays = new List<PlayRecord>();
            var length = generator.Next(0, 60);
            for (var i = 0; i < length; i++)
            {
                plays.Add(APlay(
                    userId: generator.Next(0, 2) == 0 ? Mine : Theirs,
                    itemId: AnIdentifier(generator.Next(0, 6)),
                    parentId: generator.Next(0, 3) == 0 ? null : AnIdentifier(100 + generator.Next(0, 3)),
                    itemName: "Item " + generator.Next(0, 6),
                    startedUtc: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                        .AddMinutes(generator.Next(0, 1_100_000)),
                    watched: TimeSpan.FromSeconds(generator.Next(0, 7200)),
                    reachedTheEnd: generator.Next(0, 2) == 0,
                    method: (PlayMethod)generator.Next(0, 4)));
            }

            var review = YearInReview.Over(plays, Mine, 2026, zone, 3, oldestPlayStartedUtc: null);

            var mine = plays
                .Where(play => play.UserId == Mine && YearOf(play, zone) == 2026)
                .ToList();

            Assert.Equal(2026, review.Year);
            Assert.Equal(zone.Id, review.ZoneId);
            Assert.Equal(mine.Count > 0, review.AnythingRecorded);

            if (!review.AnythingRecorded)
            {
                continue;
            }

            Assert.Equal((long)mine.Count, review.Plays);
            Assert.Equal(
                mine.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
                review.Watched);
            Assert.Equal((long)mine.Select(play => play.ItemId).Distinct().Count(), review.DistinctItems);
            Assert.Equal(mine.Max(play => play.WatchedDuration), review.LongestPlay);
            Assert.Equal((long)mine.Count(play => play.ReachedTheEnd), review.Finished);
            Assert.Equal((long)mine.Count(play => !play.ReachedTheEnd), review.Abandoned);
            Assert.Equal(review.Plays, review.Finished + review.Abandoned);
            Assert.Equal(review.Plays, review.Delivery!.Plays);
            Assert.Equal((long)mine.Count(play => play.PlayMethodAtStart == PlayMethod.Transcode), review.Delivery.Transcode);

            var byDay = mine
                .GroupBy(play => DayOf(play, zone))
                .Select(group => new
                {
                    Day = group.Key,
                    Watched = group.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration)
                })
                .OrderByDescending(day => day.Watched)
                .ThenBy(day => day.Day)
                .First();

            Assert.Equal(byDay.Day, review.BusiestDay!.Day);
            Assert.Equal(byDay.Watched, review.BusiestDay.Watched);

            var byMonth = mine
                .GroupBy(play => DayOf(play, zone).Month)
                .Select(group => new
                {
                    Month = group.Key,
                    Watched = group.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
                    Plays = (long)group.Count()
                })
                .OrderByDescending(month => month.Watched)
                .ThenBy(month => month.Month)
                .First();

            Assert.Equal(byMonth.Month, review.BusiestMonth!.Month);
            Assert.Equal(byMonth.Watched, review.BusiestMonth.Watched);
            Assert.Equal(byMonth.Plays, review.BusiestMonth.Plays);

            AssertTopListIs(
                mine.GroupBy(play => play.ItemId),
                review.TopItems,
                3);

            AssertTopListIs(
                mine.Where(play => play.ParentId is not null).GroupBy(play => play.ParentId!.Value),
                review.TopSeries,
                3);
        }
    }

    /// <summary>
    /// The third condition of issue #66. The fold is handed everybody's rows and
    /// answers about one person, and the failure written against is the one that
    /// looks like a working report: a wrap-up whose arithmetic is right and
    /// whose subject is somebody else. It is asserted from both ends, that the
    /// figures are those of the person asked for and that nothing another person
    /// alone watched reaches either list.
    /// </summary>
    [Fact]
    public void OnePersonsYearIsFoldedFromTheirOwnRowsAndNobodyElses()
    {
        var ours = AnIdentifier(1);
        var only = AnIdentifier(2);
        var plays = new[]
        {
            APlay(userId: Mine, itemId: ours, watched: TimeSpan.FromMinutes(10)),
            APlay(userId: Theirs, itemId: ours, watched: TimeSpan.FromMinutes(90)),
            APlay(userId: Theirs, itemId: only, watched: TimeSpan.FromMinutes(90), parentId: AnIdentifier(9))
        };

        var review = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null);

        Assert.Equal(1L, review.Plays);
        Assert.Equal(TimeSpan.FromMinutes(10), review.Watched);
        Assert.Equal(1L, review.DistinctItems);
        Assert.Equal(TimeSpan.FromMinutes(10), review.LongestPlay);
        Assert.Equal(ours, Assert.Single(review.TopItems).Key);
        Assert.Empty(review.TopSeries);
    }

    /// <summary>
    /// The second condition of issue #66. A year that person recorded nothing in
    /// says so, and every figure is absent rather than nought, because a wrap-up
    /// of noughts is the same picture for a quiet year, a year the retention
    /// sweep emptied and a year before anything was recorded.
    /// </summary>
    [Fact]
    public void AYearWithNothingInItSaysSoRatherThanAnsweringWithNoughts()
    {
        var plays = new[] { APlay(userId: Theirs) };

        var review = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);

        Assert.False(review.AnythingRecorded);
        Assert.Equal(2026, review.Year);
        Assert.Equal(Berlin.Id, review.ZoneId);
        Assert.Null(review.Plays);
        Assert.Null(review.Watched);
        Assert.Null(review.DistinctItems);
        Assert.Null(review.LongestPlay);
        Assert.Null(review.BusiestDay);
        Assert.Null(review.BusiestMonth);
        Assert.Null(review.Finished);
        Assert.Null(review.Abandoned);
        Assert.Null(review.Delivery);
        Assert.Empty(review.TopItems);
        Assert.Empty(review.TopSeries);
    }

    /// <summary>
    /// Which year a play falls in is read in the zone handed in and not in UTC.
    /// A play at half past eleven on New Year's Eve is in the old year for a
    /// reader in London and in the new one for a reader in Berlin, and this is
    /// the boundary a wrap-up is read across every January.
    /// </summary>
    [Fact]
    public void APlayOnNewYearsEveFallsInTheYearTheReaderWasIn()
    {
        var plays = new[] { APlay(startedUtc: new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc)) };

        Assert.True(YearInReview.Over(plays, Mine, 2025, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null).AnythingRecorded);
        Assert.False(YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null).AnythingRecorded);
        Assert.False(YearInReview.Over(plays, Mine, 2025, Berlin, 10, oldestPlayStartedUtc: null).AnythingRecorded);
        Assert.True(YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null).AnythingRecorded);
        Assert.False(YearInReview.Over(plays, Mine, 2025, Auckland, 10, oldestPlayStartedUtc: null).AnythingRecorded);
    }

    /// <summary>
    /// A play whose start is not in UTC is refused rather than read in the
    /// machine's zone. It is the failure that leaves no trace: the year would be
    /// a real year and the figures would still add up, and the whole wrap-up
    /// would be shifted by the offset of whichever machine answered, which at a
    /// year's edges is one play leaving and another arriving.
    /// </summary>
    /// <remarks>
    /// The second start is in a year nobody asked about, and it is the case the
    /// refusal has to be here for. A play that reaches the daily fold meets the
    /// same refusal there, but one this fold decides is outside the year never
    /// reaches it, so without a refusal at the boundary reading the play is
    /// dropped in silence and the wrap-up is short by however many rows a badly
    /// written store handed over.
    /// </remarks>
    [Theory]
    [InlineData(DateTimeKind.Local, 2026)]
    [InlineData(DateTimeKind.Unspecified, 2026)]
    [InlineData(DateTimeKind.Local, 2020)]
    [InlineData(DateTimeKind.Unspecified, 2020)]
    public void AStartThatIsNotUtcIsRefusedRatherThanReadInTheMachinesZone(DateTimeKind kind, int startedIn)
    {
        var started = new DateTime(startedIn, 6, 15, 12, 0, 0, kind);
        var plays = new[] { APlay(startedUtc: started) };

        Assert.Throws<ArgumentException>(() => YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null));
    }

    /// <summary>
    /// A top list with no bound is a top list that is the whole year on a server
    /// that recorded a lot of it, so the bound is an argument and a number that
    /// cannot bound anything is refused rather than quietly read as all of them.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ATopListBoundThatBoundsNothingIsRefused(int topCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => YearInReview.Over(Array.Empty<PlayRecord>(), Mine, 2026, Berlin, topCount, oldestPlayStartedUtc: null));
    }

    /// <summary>
    /// The lists are cut to the bound and are the top of the order rather than
    /// whichever rows the fold met first, and a bound larger than the year has
    /// rows for leaves the list whole rather than padding it.
    /// </summary>
    [Fact]
    public void TheListsHoldTheMostWatchedUpToTheBoundAndNoMore()
    {
        var plays = new[]
        {
            APlay(itemId: AnIdentifier(1), watched: TimeSpan.FromMinutes(10)),
            APlay(itemId: AnIdentifier(2), watched: TimeSpan.FromMinutes(50)),
            APlay(itemId: AnIdentifier(3), watched: TimeSpan.FromMinutes(30))
        };

        var cut = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 2, oldestPlayStartedUtc: null);

        Assert.Equal(
            new[] { AnIdentifier(2), AnIdentifier(3) },
            cut.TopItems.Select(row => row.Key).ToArray());

        var whole = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 9, oldestPlayStartedUtc: null);

        Assert.Equal(3, whole.TopItems.Count);
    }

    /// <summary>
    /// Two items with the same watched time are ordered by their identifiers and
    /// never by whichever the fold happened to meet first. A dictionary's order
    /// is not a promise, so a list left to it changes between two readings of
    /// one unchanged year, which reads as data that moved.
    /// </summary>
    [Fact]
    public void ItemsWithEqualWatchedTimeAreOrderedByIdentifierAndNotByArrival()
    {
        var plays = new[]
        {
            APlay(itemId: AnIdentifier(9), watched: TimeSpan.FromMinutes(20)),
            APlay(itemId: AnIdentifier(4), watched: TimeSpan.FromMinutes(20))
        };

        var review = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null);

        Assert.Equal(
            new[] { AnIdentifier(4), AnIdentifier(9) },
            review.TopItems.Select(row => row.Key).ToArray());
    }

    /// <summary>
    /// An item renamed during the year is one row called what it is called now,
    /// decided by the start on the row rather than by the order the rows
    /// arrived in, so a store that answered in a different order gives the same
    /// label. An item nothing named carries no name rather than a label made of
    /// the spaces the server sent.
    /// </summary>
    [Fact]
    public void AnItemRenamedDuringTheYearIsOneRowUnderItsLatestName()
    {
        var renamed = AnIdentifier(1);
        var plays = new[]
        {
            APlay(itemId: renamed, itemName: "The later name", startedUtc: Noon.AddDays(30)),
            APlay(itemId: renamed, itemName: "The earlier name", startedUtc: Noon),
            APlay(itemId: AnIdentifier(2), itemName: "   ", watched: TimeSpan.FromSeconds(1))
        };

        var review = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null);

        var rows = review.TopItems.ToDictionary(row => row.Key, row => row.Name);

        Assert.Equal("The later name", rows[renamed]);
        Assert.Null(rows[AnIdentifier(2)]);
        Assert.Equal(2L, review.TopItems.Single(row => row.Key == renamed).Plays);
    }

    /// <summary>
    /// Episodes count under the series they belong to, and a play of something
    /// that belongs to nothing is in no series row rather than in one of its
    /// own. Every series row carries no name, because the row keeps the name of
    /// the item and no name for its parent, and a series called after one of its
    /// episodes would be a label a reader cannot tell from a real one.
    /// </summary>
    [Fact]
    public void EpisodesCountUnderTheirSeriesAndTheSeriesIsCountedWithoutAName()
    {
        var series = AnIdentifier(7);
        var plays = new[]
        {
            APlay(itemId: AnIdentifier(1), parentId: series, watched: TimeSpan.FromMinutes(20), itemName: "One"),
            APlay(itemId: AnIdentifier(2), parentId: series, watched: TimeSpan.FromMinutes(20), itemName: "Two"),
            APlay(itemId: AnIdentifier(3), parentId: null, watched: TimeSpan.FromMinutes(90), itemName: "A film")
        };

        var review = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null);

        var row = Assert.Single(review.TopSeries);

        Assert.Equal(series, row.Key);
        Assert.Null(row.Name);
        Assert.Equal(2L, row.Plays);
        Assert.Equal(TimeSpan.FromMinutes(40), row.Watched);
        Assert.Equal(3, review.TopItems.Count);
    }

    /// <summary>
    /// The busiest day and the busiest month are the ones with the most watched
    /// time, and where two are level the earlier one is named. A tie broken the
    /// other way tells somebody their year peaked in December when the same
    /// figure was reached in March, and the reading a person expects of "the day
    /// I watched most" is the first time they got there.
    /// </summary>
    [Fact]
    public void TheBusiestDayAndMonthAreTheMostWatchedAndTiesGoToTheEarlier()
    {
        var march = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var plays = new[]
        {
            APlay(startedUtc: march, watched: TimeSpan.FromMinutes(60)),
            APlay(startedUtc: march.AddDays(1), watched: TimeSpan.FromMinutes(15)),
            APlay(startedUtc: march.AddDays(300), watched: TimeSpan.FromMinutes(60))
        };

        var review = YearInReview.Over(plays, Mine, 2026, TimeZoneInfo.Utc, 10, oldestPlayStartedUtc: null);

        Assert.Equal(new DateOnly(2026, 3, 4), review.BusiestDay!.Day);
        Assert.Equal(TimeSpan.FromMinutes(60), review.BusiestDay.Watched);
        Assert.Equal(3, review.BusiestMonth!.Month);
        Assert.Equal(TimeSpan.FromMinutes(75), review.BusiestMonth.Watched);
        Assert.Equal(2L, review.BusiestMonth.Plays);
    }

    /// <summary>
    /// The days a wrap-up names are the rows of the fold the daily chart is
    /// drawn from, over the same plays and in the same zone, so a person reading
    /// their busiest day beside that chart cannot be shown two answers. It is
    /// asserted against the other fold rather than against a number typed here,
    /// because a number typed here would agree with whichever of the two was
    /// written second.
    /// </summary>
    [Fact]
    public void TheBusiestDayIsARowOfTheFoldTheDailyChartIsDrawnFrom()
    {
        var plays = new[]
        {
            APlay(startedUtc: new DateTime(2026, 7, 1, 22, 30, 0, DateTimeKind.Utc), watched: TimeSpan.FromMinutes(45)),
            APlay(startedUtc: new DateTime(2026, 7, 2, 22, 30, 0, DateTimeKind.Utc), watched: TimeSpan.FromMinutes(20))
        };

        var review = YearInReview.Over(plays, Mine, 2026, Auckland, 10, oldestPlayStartedUtc: null);
        var daily = DailyUsage.Over(plays, Auckland);

        Assert.Contains(review.BusiestDay, daily.Rows);
        Assert.Equal(daily.ZoneId, review.ZoneId);
        Assert.Equal(daily.Watched, review.Watched);
        Assert.Equal(daily.Plays, review.Plays);
    }

    /// <summary>
    /// A top list, derived here the way the issue words it rather than the way
    /// the fold is written, so the two agree by measurement and not by sharing a
    /// line of code.
    /// </summary>
    private static void AssertTopListIs(
        IEnumerable<IGrouping<Guid, PlayRecord>> grouped,
        IReadOnlyList<TitleRow> rows,
        int topCount)
    {
        var expected = grouped
            .Select(group => new
            {
                Key = group.Key,
                Plays = (long)group.Count(),
                Watched = group.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration)
            })
            .OrderByDescending(row => row.Watched)
            .ThenBy(row => row.Key)
            .Take(topCount)
            .ToList();

        Assert.Equal(expected.Count, rows.Count);

        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Key, rows[i].Key);
            Assert.Equal(expected[i].Plays, rows[i].Plays);
            Assert.Equal(expected[i].Watched, rows[i].Watched);
        }
    }

    /// <summary>
    /// The year a play falls in, derived the way a reader reads the issue rather
    /// than the way the fold is written.
    /// </summary>
    private static int YearOf(PlayRecord play, TimeZoneInfo zone) => DayOf(play, zone).Year;

    private static DateOnly DayOf(PlayRecord play, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(new DateTimeOffset(play.StartedUtc), zone).DateTime);

    private static Guid AnIdentifier(int seed) =>
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    private static DateTime Noon => new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private static PlayRecord APlay(
        Guid? userId = null,
        Guid? itemId = null,
        Guid? parentId = null,
        string itemName = "An episode",
        DateTime? startedUtc = null,
        TimeSpan? watched = null,
        bool reachedTheEnd = true,
        PlayMethod method = PlayMethod.DirectPlay) => new()
        {
            SchemaVersion = 1,
            UserId = userId ?? Mine,
            ItemId = itemId ?? AnIdentifier(1),
            ItemType = "Episode",
            ParentId = parentId,
            ItemName = itemName,
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = startedUtc ?? Noon,
            EndedUtc = (startedUtc ?? Noon).AddMinutes(41),
            WatchedDuration = watched ?? TimeSpan.FromMinutes(38),
            ReachedTheEnd = reachedTheEnd,
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
