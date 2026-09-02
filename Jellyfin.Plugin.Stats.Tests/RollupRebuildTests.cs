// A rollup that cannot be produced again from the play rows is the only copy of
// what it holds, and one that has drifted from them is worse than no rollup at
// all, because it is believed. Both are read the same way: rebuild and compare.
// Issue #253.
//
// The month below is generated rather than hand-written, from a fixed seed, so
// the comparison is over a month of plays across several accounts, kinds of
// item, clients and delivery methods rather than over a case somebody chose to
// be easy. The seed is fixed because a case that generated a different month on
// every run would be a case nobody could reproduce a failure of.
//
// Every case drives a real store over a temporary directory. The incremental
// fold happens as rows are written and the rebuild reads them back, so a fake
// would be comparing one piece of test code against another.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class RollupRebuildTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid Carol = Guid.Parse("9f7c1c2e-0d3a-4b58-9f31-2a6d5e8b7c04");

    /// <summary>
    /// The first moment of the generated month, fixed rather than read off a
    /// clock. March has thirty-one days, so an arithmetic slip of a month is a
    /// different answer from a slip of thirty days.
    /// </summary>
    private static readonly DateTime FirstOfMarch = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// An aggregate window nothing here falls outside of. These cases are
    /// about the play rows, and a sweep that took the days they were folded
    /// into as well would be proving two windows at once.
    /// </summary>
    private static readonly DateTime KeepEveryRollup = DateTime.UnixEpoch;

    /// <summary>
    /// The zone every case here keys its days in. Named rather than left to the
    /// machine, so a runner in another zone reads the same days, and one with a
    /// summer change inside March so a day of the month is not a fixed number of
    /// hours.
    /// </summary>
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// How many plays a case seeds unless it says otherwise. Large enough that
    /// the month folds into a hundred rows or so over three accounts, three
    /// kinds of item and three clients, and small enough that seven cases
    /// writing one do not dominate the suite.
    /// </summary>
    private const int PlaysInTheMonth = 120;

    /// <summary>
    /// What the paging case seeds instead, which is past one page of the
    /// rebuild. Only that case pays for it.
    /// </summary>
    private const int PlaysPastOnePage = 520;

    private readonly string _root;

    public RollupRebuildTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #253. A rebuild from the play rows produces
    /// the rollups the incremental fold produced, over a generated month.
    /// </summary>
    [Fact]
    public void ARebuildProducesTheRollupsTheFoldProduced()
    {
        SeedAMonth();

        var incremental = Rollups();

        // Not an empty comparison dressed up as one. The seeded month folds
        // into a hundred rows or so over three accounts, three kinds of item
        // and three clients, and a rebuild that produced nothing would
        // otherwise match a store whose fold had produced nothing either.
        Assert.True(incremental.Count > 20, "The generated month folded into " + incremental.Count + " rows, which is too few for the comparison below to be about anything.");

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Equal(incremental, Rollups());
    }

    /// <summary>
    /// The rebuild replaces what is there rather than adding to it. A rebuild
    /// that folded on top of the existing rows would double every figure and
    /// still produce a table of the right shape.
    /// </summary>
    [Fact]
    public void ARebuildReplacesTheRollupsRatherThanAddingToThem()
    {
        var seeded = SeedAMonth();

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
            store.RebuildRollups();
        }

        var twice = Rollups();

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Equal(twice, Rollups());
        Assert.Equal(seeded, twice.Sum(rollup => rollup.Plays));
    }

    /// <summary>
    /// The second condition of issue #253. A corrective deletion reaches the
    /// rollups that counted the rows it removed, asserted by rebuilding
    /// afterwards and finding the same figures.
    /// </summary>
    [Fact]
    public void ACorrectiveDeletionMovesTheRollupsThatCountedThoseRows()
    {
        SeedAMonth();

        var before = Rollups();

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite).Delete(Bob, null, null);

        var after = Rollups();

        // The figures moved, and the account is out of the table entirely.
        Assert.NotEqual(before, after);
        Assert.DoesNotContain(after, rollup => rollup.UserId.Equals(Bob));
        Assert.Contains(after, rollup => rollup.UserId.Equals(Alice));

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Equal(after, Rollups());
    }

    /// <summary>
    /// The same over a window rather than a whole history, which is the case
    /// where a rollup has to be moved rather than removed: the days at the edges
    /// of the window keep the plays that fell outside it.
    /// </summary>
    [Fact]
    public void ACorrectiveDeletionOfAWindowLeavesTheRestOfEachDayStanding()
    {
        SeedAMonth();

        var fromUtc = FirstOfMarch.AddDays(6);
        var toUtc = FirstOfMarch.AddDays(9);

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite)
            .Delete(Alice, fromUtc, toUtc);

        var after = Rollups();

        Assert.Contains(after, rollup => rollup.UserId.Equals(Alice));

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Equal(after, Rollups());
    }

    /// <summary>
    /// A corrective deletion moves the row the removed play was folded into and
    /// no neighbouring row. Four things name a rollup row - the day, the
    /// account, the kind of item and the client - and a subtraction that left
    /// any one of them out would take a play away from days it was never in.
    /// </summary>
    /// <remarks>
    /// The generated month cannot catch this. Its plays are spread over
    /// thirty-one days, three accounts, three kinds of item and three clients,
    /// so almost every row it produces stands alone and a subtraction reaching
    /// its neighbours has none to reach. This plants them.
    /// </remarks>
    [Fact]
    public void ACorrectiveDeletionMovesOneRowAndNotItsNeighbours()
    {
        var noon = FirstOfMarch.AddDays(3).AddHours(12);

        using (var store = OpenTheStore())
        {
            // The one the deletion is about, and three that differ from it by
            // exactly one of the four columns that name a row.
            store.Add(APlay(Carol, noon, "Movie", "Jellyfin Web", PlayMethod.DirectPlay));
            store.Add(APlay(Carol, noon.AddMinutes(30), "Movie", "Jellyfin Web", PlayMethod.DirectPlay));
            store.Add(APlay(Carol, noon.AddMinutes(10), "Movie", "Android TV", PlayMethod.DirectPlay));
            store.Add(APlay(Carol, noon.AddMinutes(20), "Episode", "Jellyfin Web", PlayMethod.DirectPlay));
            store.Add(APlay(Carol, noon.AddDays(1), "Movie", "Jellyfin Web", PlayMethod.DirectPlay));
        }

        Assert.Equal(4, Rollups().Count);

        // A window holding the first play alone. The others start later in the
        // hour, so what the window catches is one row's worth and not a moment
        // three plays share.
        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite)
            .Delete(Carol, noon.AddMinutes(-1), noon.AddMinutes(1));

        var after = Rollups();

        Assert.Equal(4, after.Count);
        Assert.All(after, rollup => Assert.Equal(1, rollup.Plays));

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Equal(after, Rollups());
    }

    /// <summary>
    /// A day a corrective deletion emptied stops being a day. A row reading
    /// nought plays is not the same statement as no row: the first says the
    /// account watched nothing that day, which a report would draw.
    /// </summary>
    [Fact]
    public void ADayACorrectiveDeletionEmptiedIsNotADayWithNothingInIt()
    {
        var play = APlay(Carol, FirstOfMarch.AddHours(20), "Movie", "Jellyfin Web", PlayMethod.Transcode);

        using (var store = OpenTheStore())
        {
            store.Add(play);
        }

        Assert.Single(Rollups());

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite).Delete(Carol, null, null);

        Assert.Empty(Rollups());
    }

    /// <summary>
    /// The third condition of issue #253. A retention deletion leaves the
    /// rollups it aged out of standing, asserted over a store whose raw rows are
    /// gone and whose aggregates are not.
    /// </summary>
    /// <remarks>
    /// This is what the longer aggregate window is for. The daily sweep at the
    /// default ninety days would otherwise empty aggregates about three hundred
    /// days before their own expiry, on every installation running defaults, and
    /// take that setting out of service without anybody deciding to remove it.
    /// </remarks>
    [Fact]
    public void ARetentionSweepLeavesTheRollupsItAgedOutOfStanding()
    {
        SeedAMonth();

        var before = Rollups();

        new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite)
            .Run(FirstOfMarch.AddDays(40), KeepEveryRollup, new Progress<double>(), CancellationToken.None);

        using (var store = OpenTheStore())
        {
            Assert.Empty(store.AllPlays());
        }

        Assert.Equal(before, Rollups());
    }

    /// <summary>
    /// The bound on the rebuild, stated as a case so nobody mistakes it for a
    /// defect. A rebuild reads the play rows, so on a store the retention sweep
    /// has emptied it produces nothing, and running one there throws away every
    /// figure the sweep deliberately left standing.
    /// </summary>
    [Fact]
    public void ARebuildOnASweptStoreProducesTheDaysItStillHasRowsFor()
    {
        SeedAMonth();

        new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite)
            .Run(FirstOfMarch.AddDays(40), KeepEveryRollup, new Progress<double>(), CancellationToken.None);

        Assert.NotEmpty(Rollups());

        using (var store = OpenTheStore())
        {
            store.RebuildRollups();
        }

        Assert.Empty(Rollups());
    }

    /// <summary>
    /// A store with no plays rebuilds to no rollups rather than refusing.
    /// </summary>
    [Fact]
    public void ARebuildOverAStoreWithNoPlaysLeavesNothing()
    {
        using var store = OpenTheStore();

        store.RebuildRollups();

        Assert.Empty(store.AllRollups());
    }

    /// <summary>
    /// The rebuild reads the rows a page at a time, so a store holding more
    /// plays than one page produces the same answer as one holding fewer. A
    /// walk that stopped at its first page would pass every case above.
    /// </summary>
    [Fact]
    public void ARebuildReadsPastItsFirstPage()
    {
        var seeded = SeedAMonth(PlaysPastOnePage);

        using (var store = OpenTheStore())
        {
            Assert.True(store.AllPlays().Count() > 500, "The seeded month is not past one page of the rebuild, so this case would prove nothing.");
            store.RebuildRollups();
        }

        Assert.Equal(seeded, Rollups().Sum(rollup => rollup.Plays));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private List<DailyRollup> Rollups()
    {
        using var store = OpenTheStore();

        return store.AllRollups().ToList();
    }

    private SqlitePlayStore OpenTheStore() => new(_root, Zone);

    /// <summary>
    /// A month of plays over three accounts, three kinds of item, three clients
    /// and every delivery method, drawn from a fixed seed so the month is the
    /// same on every run and on every machine.
    /// </summary>
    /// <param name="plays">How many to write.</param>
    /// <returns>How many were written.</returns>
    private int SeedAMonth(int plays = PlaysInTheMonth)
    {
        var accounts = new[] { Alice, Bob, Carol };
        var kinds = new[] { "Movie", "Episode", "Audio" };
        var clients = new[] { "Jellyfin Web", "Android TV", "iOS" };
        var methods = new[] { PlayMethod.DirectPlay, PlayMethod.DirectStream, PlayMethod.Transcode, PlayMethod.Unknown };

        var draw = new Random(20260301);

        using var store = OpenTheStore();

        for (var i = 0; i < plays; i++)
        {
            // Spread over the month by the minute rather than by the day, so
            // plays land on both sides of local midnight and inside the hour
            // the zone moves its clocks. A day that is not twenty-four hours
            // long is where a rebuild and a fold would disagree if either of
            // them worked the day out for itself.
            var started = FirstOfMarch.AddMinutes(draw.Next(0, 31 * 24 * 60));

            store.Add(APlay(
                accounts[draw.Next(accounts.Length)],
                started,
                kinds[draw.Next(kinds.Length)],
                clients[draw.Next(clients.Length)],
                methods[draw.Next(methods.Length)],
                TimeSpan.FromMinutes(draw.Next(1, 120)),
                draw.Next(2) == 1));
        }

        return plays;
    }

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        string itemType,
        string clientName,
        PlayMethod method,
        TimeSpan? watched = null,
        bool reachedTheEnd = false)
    {
        var duration = watched ?? TimeSpan.FromMinutes(38);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = itemType,
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc + duration,
            WatchedDuration = duration,
            ReachedTheEnd = reachedTheEnd,
            ClientName = clientName,
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = method,
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
}
