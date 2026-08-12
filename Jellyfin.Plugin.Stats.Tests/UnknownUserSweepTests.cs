// The sweep that removes rows whose account the server no longer has, driven
// over a temporary directory. Nothing here needs a server: the store takes the
// folder it writes into as an argument, and the accounts arrive through a fake
// user manager the test owns.
//
// Every case that asserts a deletion reads the rows back through a store opened
// afresh over the same file, and through AllPlays, which answers to nobody's
// filter. That is what tells a deletion from a row the read happened to skip.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class UnknownUserSweepTests : IDisposable
{
    /// <summary>
    /// The account the server still has in every case here.
    /// </summary>
    private static readonly Guid Ada = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    /// <summary>
    /// The account the server has and that has never played anything, which is
    /// the case the second condition of issue #45 is written against.
    /// </summary>
    private static readonly Guid Bo = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>
    /// The account whose rows are in the store and whose user the server does
    /// not have. Deleted while this plugin was not loaded, which is the whole
    /// reason this sweep exists.
    /// </summary>
    private static readonly Guid Removed = Guid.Parse("018f3a1e-2b7c-7a41-9d9c-2f0f8f5a1c33");

    private readonly string _root;

    public UnknownUserSweepTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first two conditions of issue #45 in one store, because separating
    /// them lets a sweep pass both while doing the wrong thing: a task that
    /// deletes everything passes the first, and a task that deletes nothing
    /// passes the second.
    /// <para>
    /// The account that has never played anything is in the server's set and
    /// has no rows, which is what the second condition names. It cannot be
    /// asserted by itself for the same reason: a sweep that deleted nothing
    /// would leave its rows alone by doing nothing at all, so the row that has
    /// to survive is the one belonging to the account that did play.
    /// </para>
    /// </summary>
    [Fact]
    public void RowsOfAnAccountTheServerNoLongerHasGoAndNobodyElsesDo()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Removed));
            store.Add(APlayBy(Ada));
        }

        var deleted = ASweep(TheServerHaving(Ada, Bo)).Run(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(1, deleted);
        Assert.Empty(after.PlaysFor(Removed));
        Assert.Equal(new[] { Ada, Ada }, after.AllPlays().Select(play => play.UserId));
    }

    /// <summary>
    /// The third condition of issue #45. The number the sweep reports is the
    /// number of rows that left the store, read back off the store rather than
    /// taken from the sweep's own arithmetic.
    /// </summary>
    [Fact]
    public void TheCountReportedIsTheNumberOfRowsRemoved()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            for (var i = 0; i < 3; i++)
            {
                store.Add(APlayBy(Removed));
            }

            store.Add(APlayBy(Ada));
        }

        long before;
        using (var counted = new SqlitePlayStore(_root))
        {
            before = counted.AllPlays().Count();
        }

        var reported = ASweep(TheServerHaving(Ada)).Run(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(before - after.AllPlays().Count(), reported);
        Assert.Equal(3, reported);
    }

    /// <summary>
    /// A run that finds nothing reports nothing and leaves every row where it
    /// was. This is what almost every run of this task on a healthy server
    /// does, so it is the path that runs most often.
    /// </summary>
    [Fact]
    public void AStoreWhoseAccountsAllStillExistKeepsEveryRow()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
        }

        var reported = new RecordingProgress();
        var deleted = ASweep(TheServerHaving(Ada, Bo)).Run(reported, CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(0, deleted);
        Assert.Single(after.AllPlays());
        Assert.Equal(new[] { 0d, 100d }, reported.Values);
    }

    /// <summary>
    /// A set larger than one bite goes entirely. The statement the store runs
    /// is bounded, so a sweep that ran it once per account would leave the
    /// remainder behind and report a number that looked like a deletion.
    /// </summary>
    [Fact]
    public void MoreRowsThanOneBiteAllGo()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            for (var i = 0; i < 5; i++)
            {
                store.Add(APlayBy(Removed));
            }

            store.Add(APlayBy(Ada));
        }

        var deleted = ASweep(TheServerHaving(Ada), bite: 2).Run(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(5, deleted);
        Assert.Empty(after.PlaysFor(Removed));
        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// A lookup that fails is not an account that is gone. The sweep asks about
    /// every identifier before it deletes anything, so a user manager that
    /// throws part way through costs a run rather than the rows it had already
    /// found an answer for. Those rows cannot be recovered: they are gone from
    /// the file and this plugin keeps no second copy.
    /// <para>
    /// The order is the case rather than a detail of it. A sweep that deleted
    /// as it walked would have taken the first account's rows before it ever
    /// reached the lookup that fails, so the account the server does not have
    /// is the one the store names first and the failure comes after it. That
    /// premise is asserted rather than assumed, because it is the identifiers
    /// that decide it and a different pair would leave this case passing
    /// against a sweep it is meant to refuse.
    /// </para>
    /// </summary>
    [Fact]
    public void ALookupThatFailsDeletesNothingAtAll()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Removed));
            store.Add(APlayBy(Ada));
        }

        using (var reading = new SqlitePlayStore(_root))
        {
            Assert.Equal(new[] { Removed, Ada }, reading.UserIdsWithPlays());
        }

        var server = TheServerHaving(Bo);
        server.Failing = id => id.Equals(Ada)
            ? new InvalidOperationException("The user database could not be read.")
            : null;

        Assert.Throws<InvalidOperationException>(
            () => ASweep(server).Run(new IgnoredProgress(), CancellationToken.None));

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(2, after.AllPlays().Count());
    }

    /// <summary>
    /// The rows are gone from the file rather than from the table alone. A
    /// delete leaves the bytes in a page nothing points at any more, and rows
    /// belonging to an account the server has forgotten are exactly the ones
    /// that may not sit there.
    /// </summary>
    [Fact]
    public void TheSpaceTheRowsHeldIsGivenBack()
    {
        var store = new CountingPlayStore(Removed, rows: 1);

        var deleted = ASweepOver(store, TheServerHaving(Ada)).Run(new IgnoredProgress(), CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(1, store.Reclaims);
        Assert.True(store.Disposed);
    }

    /// <summary>
    /// A run that deleted nothing costs no rewrite of the file. That is what
    /// this task does on almost every run it ever makes, and a rewrite of the
    /// whole store to reclaim nothing is the whole cost of a sweep for no
    /// reason, every night.
    /// </summary>
    [Fact]
    public void ARunThatFindsNothingLeavesTheFileAlone()
    {
        var store = new CountingPlayStore(Ada, rows: 3);

        var deleted = ASweepOver(store, TheServerHaving(Ada)).Run(new IgnoredProgress(), CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(0, store.Reclaims);
        Assert.Equal(0, store.Deletions);
    }

    /// <summary>
    /// A cancelled sweep stops. It is checked between lookups and between
    /// bites, so a first run on a store holding years of rows for accounts that
    /// are gone is a run an administrator can stop.
    /// </summary>
    [Fact]
    public void ACancelledSweepStopsRatherThanFinishing()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Removed));
        }

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => ASweep(TheServerHaving(Ada)).Run(new IgnoredProgress(), cancelled.Token));

        using var after = new SqlitePlayStore(_root);

        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// The store answers with one entry per account rather than one per row,
    /// which is what lets this read carry no bound. A sweep over the same set
    /// read out of the rows would ask the server the same question once per
    /// play.
    /// </summary>
    [Fact]
    public void TheStoreNamesEachAccountOnce()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Removed));
            store.Add(APlayBy(Ada));
        }

        using var reading = new SqlitePlayStore(_root);

        Assert.Equal(
            new[] { Ada, Removed }.OrderBy(id => id.ToString("N"), StringComparer.Ordinal),
            reading.UserIdsWithPlays());
    }

    /// <summary>
    /// An empty store names nobody, so the sweep asks the server nothing. This
    /// is a fresh installation, and the read that would otherwise be the first
    /// thing to run against a file with no rows in it.
    /// </summary>
    [Fact]
    public void AnEmptyStoreNamesNobody()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Empty(store.UserIdsWithPlays());
    }

    /// <summary>
    /// The server builds every scheduled task in a plugin assembly out of its
    /// own container and fails the whole plugin over an argument it cannot
    /// resolve, so what this asserts is that the task can be constructed the
    /// way the server constructs it.
    /// <para>
    /// The user manager is registered here because the server registers it, and
    /// this plugin's own registrations are what the rest comes from. Nothing in
    /// this opens a store: the store-opening function is handed on rather than
    /// run.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTaskIsBuiltTheWayTheServerBuildsIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserManager>(new FakeUserManager());

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        var task = ActivatorUtilities.CreateInstance<UnknownUserSweepTask>(provider);

        Assert.NotEmpty(task.Name);
        Assert.NotEmpty(task.Key);
        Assert.NotEmpty(task.Description);
        Assert.NotEmpty(task.Category);

        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(4).Ticks, trigger.TimeOfDayTicks);
    }

    /// <summary>
    /// The key the server files this task's triggers under is not the retention
    /// sweep's. Two tasks in one assembly sharing a key is a collision inside
    /// this plugin before it is one with anybody else's, and it costs one of
    /// the two tasks.
    /// </summary>
    [Fact]
    public void TheTwoTasksInThisPluginDoNotShareAKey()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserManager>(new FakeUserManager());

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        var reconciliation = ActivatorUtilities.CreateInstance<UnknownUserSweepTask>(provider);
        var retention = ActivatorUtilities.CreateInstance<RetentionSweepTask>(provider);

        Assert.NotEqual(retention.Key, reconciliation.Key);
        Assert.NotEqual(retention.Name, reconciliation.Name);
    }

    /// <summary>
    /// The task runs the sweep it was given rather than one of its own, and
    /// what it hands on is the progress and the cancellation the server gave
    /// it.
    /// </summary>
    [Fact]
    public async Task TheTaskRunsTheSweep()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Removed));
        }

        var reported = new RecordingProgress();

        await new UnknownUserSweepTask(ASweep(TheServerHaving(Ada)))
            .ExecuteAsync(reported, CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.AllPlays());
        Assert.Equal(100d, reported.Values[^1]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static FakeUserManager TheServerHaving(params Guid[] users)
        => new(users.Select(id => FakeUserManager.NewUser("someone", id)).ToArray());

    private static UnknownUserSweep ASweepOver(IPlayStore store, IUserManager users)
        => new(() => store, users, UnknownUserSweep.DefaultBite);

    private static PlayRecord APlayBy(Guid userId)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.DirectPlay,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    private UnknownUserSweep ASweep(IUserManager users, int bite = UnknownUserSweep.DefaultBite)
        => new(() => new SqlitePlayStore(_root), users, bite);

    /// <summary>
    /// A progress reporter that keeps what it was told, so a case can assert
    /// that a run said it had finished.
    /// </summary>
    private sealed class RecordingProgress : IProgress<double>
    {
        private readonly List<double> _values = new();

        public IReadOnlyList<double> Values => _values;

        public void Report(double value) => _values.Add(value);
    }

    /// <summary>
    /// A progress reporter for the cases that are not about progress.
    /// </summary>
    private sealed class IgnoredProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }

    /// <summary>
    /// A store that holds rows for one account and counts what was asked of it.
    /// The two cases about the reclaim are about a call being made or not made,
    /// and a real store answers both of them with a file whose size a test would
    /// then have to read a meaning into.
    /// </summary>
    private sealed class CountingPlayStore : IPlayStore
    {
        private readonly Guid _userId;
        private int _rowsLeft;

        public CountingPlayStore(Guid userId, int rows)
        {
            _userId = userId;
            _rowsLeft = rows;
        }

        /// <summary>
        /// Gets how many times a deletion was asked for, including the last one
        /// that found nothing left.
        /// </summary>
        public int Deletions { get; private set; }

        /// <summary>
        /// Gets how many times the space was given back.
        /// </summary>
        public int Reclaims { get; private set; }

        /// <summary>
        /// Gets whether the sweep disposed of this store.
        /// </summary>
        public bool Disposed { get; private set; }

        public IReadOnlyList<Guid> UserIdsWithPlays() => new[] { _userId };

        public int DeletePlaysFor(Guid userId, int limit)
        {
            Deletions++;

            var taken = Math.Min(_rowsLeft, limit);
            _rowsLeft -= taken;

            return taken;
        }

        public void ReclaimFreedSpace() => Reclaims++;

        public void Dispose() => Disposed = true;

        public void Add(PlayRecord play) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("A reconciliation reads the identifiers and deletes by one, so this fake answers nothing else.");
    }
}
