// What a top list may say, what it counts under one row, and about whom.
// Issue #52.
//
// A top list is where an aggregate view most easily stops being aggregate. On a
// small server the most-watched item is usually a statement about one person,
// and what is published there is not who watched but that somebody did, which
// on a server with three accounts is the same sentence. The rule is the one
// decided for the whole plugin on issue #41 and already carried by the
// breakdown, so these cases are written against the top list carrying it too
// rather than against a rule invented for this shape.
//
// Driven over a real store for the reason the neighbouring shape cases are: a
// fake answering from a list would prove the folding and say nothing about the
// read that fetched the rows.

using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class TopListsOverARangeTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AnEpisode = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly Guid AnotherEpisode = Guid.Parse("11111111-0000-0000-0000-000000000002");

    private static readonly Guid AFilm = Guid.Parse("11111111-0000-0000-0000-000000000003");

    private static readonly Guid TheSeries = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public TopListsOverARangeTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// The first condition of issue #52. The list is a fact about the range and
    /// not about whoever asked: the two accounts here have different histories,
    /// and both of them are in the one answer this shape gives.
    /// </summary>
    /// <remarks>
    /// The shape takes no account at all, so there is no caller to vary and the
    /// property holds by construction. What is asserted instead is what that
    /// construction buys: the answer holds rows neither account alone could
    /// have produced, and two readings of the same range are the same list.
    /// </remarks>
    [Fact]
    public void TheSameRangeGivesOneListWhateverEitherAccountWatched()
    {
        Store(
            APlay(Alice, AFilm, March, watched: TimeSpan.FromMinutes(90)),
            APlay(Bob, AFilm, March.AddHours(1), watched: TimeSpan.FromMinutes(90)),
            APlay(Alice, AnEpisode, March.AddHours(2), TheSeries, TimeSpan.FromMinutes(20)),
            APlay(Bob, AnEpisode, March.AddHours(3), TheSeries, TimeSpan.FromMinutes(20)));

        var first = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10);
        var second = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first, second);
        Assert.Equal(new[] { AFilm, AnEpisode }, first.Select(row => row.Key));
    }

    /// <summary>
    /// The second condition of issue #52. An episode is itself in the item list
    /// and its series is not there; the series is a row of the series list and
    /// no episode is. The two never appear in one list because they are keyed
    /// on different columns of the row and each list reads one of them.
    /// </summary>
    [Fact]
    public void AnEpisodeAndItsSeriesAreNeverBothInOneList()
    {
        Store(
            APlay(Alice, AnEpisode, March, TheSeries, TimeSpan.FromMinutes(20)),
            APlay(Bob, AnEpisode, March.AddHours(1), TheSeries, TimeSpan.FromMinutes(20)),
            APlay(Alice, AnotherEpisode, March.AddHours(2), TheSeries, TimeSpan.FromMinutes(25)),
            APlay(Bob, AnotherEpisode, March.AddHours(3), TheSeries, TimeSpan.FromMinutes(25)));

        var queries = new AggregateQueries(OpenTheStore);

        var items = queries.Top(AWeekFrom(March), 10, TopListGrouping.Item);
        var series = queries.Top(AWeekFrom(March), 10, TopListGrouping.Series);

        Assert.NotNull(items);
        Assert.NotNull(series);

        Assert.Equal(new[] { AnotherEpisode, AnEpisode }, items.Select(row => row.Key));
        Assert.DoesNotContain(items, row => row.Key == TheSeries);

        Assert.Equal(new[] { TheSeries }, series.Select(row => row.Key));
        Assert.DoesNotContain(series, row => row.Key == AnEpisode || row.Key == AnotherEpisode);

        // The whole of the series' watching, which is both episodes added up.
        Assert.Equal(TimeSpan.FromMinutes(90), series[0].Watched);
        Assert.Equal(4, series[0].Plays);
    }

    /// <summary>
    /// The third condition of issue #52, with one account and one item, which is
    /// the smallest server the question can be asked on. The list is withheld
    /// whole rather than answered with the group size beside the row, and the
    /// total still answers: what a range came to says nothing about who was
    /// behind it, and withholding it as well would be hiding a fact nobody could
    /// have used.
    /// </summary>
    [Fact]
    public void AListWithOneAccountBehindARowIsWithheldWholeAndTheTotalStillAnswers()
    {
        Store(
            APlay(Alice, AFilm, March, watched: TimeSpan.FromMinutes(90)));

        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        Assert.Null(queries.Top(window, 10));

        var total = queries.Total(window);
        Assert.Equal(1, total.Plays);
        Assert.Equal(TimeSpan.FromMinutes(90), total.Watched);
    }

    /// <summary>
    /// One thin row withholds the whole list and not just itself. A list with
    /// the thin row dropped and everything else shown hands back the same
    /// subtraction: the rows that remain and the total beside them say what the
    /// missing one was.
    /// </summary>
    [Fact]
    public void OneThinRowWithholdsTheWholeList()
    {
        Store(
            APlay(Alice, AFilm, March, watched: TimeSpan.FromMinutes(90)),
            APlay(Bob, AFilm, March.AddHours(1), watched: TimeSpan.FromMinutes(90)),
            APlay(Alice, AnEpisode, March.AddHours(2), TheSeries, TimeSpan.FromMinutes(20)));

        Assert.Null(new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10));
    }

    /// <summary>
    /// What stands behind a row is counted in accounts and not in plays. Four
    /// hundred plays from one person are a row standing on one account, and a
    /// count taken in plays would answer this list and name that person by what
    /// they watched.
    /// </summary>
    [Fact]
    public void WhatStandsBehindARowIsCountedInAccounts()
    {
        Store(
            APlay(Alice, AFilm, March, watched: TimeSpan.FromMinutes(20)),
            APlay(Alice, AFilm, March.AddHours(1), watched: TimeSpan.FromMinutes(20)),
            APlay(Alice, AFilm, March.AddHours(2), watched: TimeSpan.FromMinutes(20)),
            APlay(Alice, AFilm, March.AddHours(3), watched: TimeSpan.FromMinutes(20)));

        Assert.Null(new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10));
    }

    /// <summary>
    /// The two orderings are different lists, which is why both are offered. The
    /// range holds a series left running as many short plays and a film watched
    /// once, and asking for one row gives a different row each way.
    /// </summary>
    [Fact]
    public void TheTwoOrderingsAreDifferentListsAndTheCutFollowsTheOrder()
    {
        Store(
            APlay(Alice, AnEpisode, March, TheSeries, TimeSpan.FromMinutes(2)),
            APlay(Alice, AnEpisode, March.AddHours(1), TheSeries, TimeSpan.FromMinutes(2)),
            APlay(Alice, AnEpisode, March.AddHours(2), TheSeries, TimeSpan.FromMinutes(2)),
            APlay(Bob, AnEpisode, March.AddHours(3), TheSeries, TimeSpan.FromMinutes(2)),
            APlay(Alice, AFilm, March.AddHours(4), watched: TimeSpan.FromMinutes(90)),
            APlay(Bob, AFilm, March.AddHours(6), watched: TimeSpan.FromMinutes(90)));

        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        var byTime = queries.Top(window, 1, TopListGrouping.Item, TopListOrder.WatchedTime);
        var byPlays = queries.Top(window, 1, TopListGrouping.Item, TopListOrder.Plays);

        Assert.NotNull(byTime);
        Assert.NotNull(byPlays);

        Assert.Equal(AFilm, Assert.Single(byTime).Key);
        Assert.Equal(AnEpisode, Assert.Single(byPlays).Key);

        // Both figures travel on every row whichever way it was ordered, so a
        // reader who wants the other reading of the rows they were handed has
        // it without a second fold.
        Assert.Equal(2, byTime[0].Plays);
        Assert.Equal(TimeSpan.FromMinutes(8), byPlays[0].Watched);
    }

    /// <summary>
    /// A play that names no series is in no series list. A film counted as a
    /// series of one is a sentence nobody asked for, and an empty identifier
    /// standing for "no series" would be one row every film piled into.
    /// </summary>
    [Fact]
    public void APlayWithNoSeriesIsInNoSeriesList()
    {
        Store(
            APlay(Alice, AFilm, March, watched: TimeSpan.FromMinutes(90)),
            APlay(Bob, AFilm, March.AddHours(1), watched: TimeSpan.FromMinutes(90)),
            APlay(Alice, AnEpisode, March.AddHours(2), TheSeries, TimeSpan.FromMinutes(20)),
            APlay(Bob, AnEpisode, March.AddHours(3), TheSeries, TimeSpan.FromMinutes(20)));

        var series = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10, TopListGrouping.Series);

        Assert.NotNull(series);
        Assert.Equal(new[] { TheSeries }, series.Select(row => row.Key));
        Assert.Equal(TimeSpan.FromMinutes(40), series[0].Watched);
    }

    /// <summary>
    /// A series row carries no name, and that is the half of issue #52 this
    /// shape is still owed a stored label for. A play keeps the name the item
    /// had and no name for its parent, so a series is counted and cannot be
    /// labelled; it is left absent rather than filled with the name of one of
    /// the episodes under it.
    /// </summary>
    [Fact]
    public void ASeriesRowIsCountedAndNotNamed()
    {
        Store(
            APlay(Alice, AnEpisode, March, TheSeries, TimeSpan.FromMinutes(20)),
            APlay(Bob, AnEpisode, March.AddHours(1), TheSeries, TimeSpan.FromMinutes(20)));

        var queries = new AggregateQueries(OpenTheStore);

        var series = queries.Top(AWeekFrom(March), 10, TopListGrouping.Series);
        var items = queries.Top(AWeekFrom(March), 10, TopListGrouping.Item);

        Assert.NotNull(series);
        Assert.NotNull(items);
        Assert.Null(series[0].Name);
        Assert.Equal("An episode", items[0].Name);
    }

    /// <summary>
    /// A range with nothing in it is an empty list and not a withheld one. There
    /// are no rows to stand on anybody, and answering with nothing says the range
    /// is empty, which is true and names nobody.
    /// </summary>
    [Fact]
    public void AnEmptyRangeIsEmptyAndNotWithheld()
    {
        Store(APlay(Alice, AFilm, March.AddDays(30), watched: TimeSpan.FromMinutes(90)));

        var top = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10);

        Assert.NotNull(top);
        Assert.Empty(top);
    }

    /// <summary>
    /// A bound that is not a positive number is refused rather than answered
    /// with an empty list, which would read as a range holding nothing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AListOfNoRowsIsRefused(int howMany)
    {
        var queries = new AggregateQueries(OpenTheStore);

        Assert.Throws<ArgumentOutOfRangeException>(() => queries.Top(AWeekFrom(March), howMany));
    }

    /// <summary>
    /// A grouping or an order this build has no name for is refused rather than
    /// falling through to whichever one the switch happens to be written with
    /// first. A value cast in from outside the two sets is how that arrives.
    /// </summary>
    /// <remarks>
    /// Over a range holding nothing, and therefore before any row could have
    /// reached the arm that reads either value. An order read where it is used
    /// would pass here and over a single row as well, because a sort of one
    /// element never asks its comparison anything, and that is how a guard ends
    /// up firing on some ranges and not others.
    /// </remarks>
    [Fact]
    public void AGroupingOrAnOrderThisBuildDoesNotKnowIsRefused()
    {
        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => queries.Top(window, 10, (TopListGrouping)7));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => queries.Top(window, 10, TopListGrouping.Item, (TopListOrder)7));
    }

    private static QueryWindow AWeekFrom(DateTime fromUtc) => QueryWindow.Of(fromUtc, fromUtc.AddDays(7));

    private static PlayRecord APlay(
        Guid userId,
        Guid itemId,
        DateTime startedUtc,
        Guid? parentId = null,
        TimeSpan? watched = null)
    {
        var length = watched ?? TimeSpan.FromMinutes(40);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = parentId is null ? "Movie" : "Episode",
            ParentId = parentId,
            ItemName = parentId is null ? "A film" : "An episode",
            ItemRuntime = TimeSpan.FromMinutes(120),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(length),
            WatchedDuration = length,
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
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

    private SqlitePlayStore OpenTheStore() => new(_root);

    private void Store(params PlayRecord[] plays)
    {
        using var store = new SqlitePlayStore(_root);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }
}
