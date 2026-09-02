// The retention sweep, driven over a temporary directory. Nothing here needs a
// server, a socket or a service: the store takes the folder it writes into as an
// argument and the sweep takes the moments it measures back from, so a test
// hands it all of them and each boundary is a value rather than the day the
// suite ran.
//
// The moment every case is written around is fixed. A row is placed one hour on
// each side of the cutoff a ninety day window produces from it, which is the
// case a sweep that was one day out would still pass and one that read the
// machine clock could not be written at all.
//
// The sweep answers two windows, and the second is written the same way: a
// rollup is keyed on the first day the aggregate window keeps and on the day
// before it, so the case sits on the boundary itself rather than at a value
// comfortably past it. Issue #315.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Model.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class RetentionSweepTests : IDisposable
{
    /// <summary>
    /// The moment every case here runs at. Fixed, and inside a month with
    /// thirty-one days, so an arithmetic slip of a month is a different answer
    /// from a slip of thirty days.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A window whose boundary the rows below straddle.
    /// </summary>
    private const int Window = 90;

    /// <summary>
    /// The aggregate window the cases below are written around. Wider than
    /// <see cref="Window"/>, which is the arrangement an installation running
    /// defaults has: the figures folded from the rows outlive the rows.
    /// </summary>
    private const int AggregateWindow = 400;

    /// <summary>
    /// A play-row window no case reaches the far side of, for the cases whose
    /// subject is the aggregate window alone. A sweep taking rows as well would
    /// leave a reader unable to say which of the two windows an answer came
    /// from.
    /// </summary>
    private const int NoPlayRowGoes = 3650;

    /// <summary>
    /// Only the columns that cannot be absent. The seeding in the file-size
    /// case is about how many bytes a store holds, and a row's optional half
    /// does not move which rows a cutoff reaches.
    /// </summary>
    private const string InsertASeededPlay =
        @"INSERT INTO plays (
              SchemaVersion, UserId, ItemId, ItemType, ItemName,
              StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
              ClientName, DeviceId, DeviceName, PlayMethodAtStart,
              TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
          ) VALUES (
              $schemaVersion, $userId, $itemId, 'Episode', 'An episode',
              $startedUtcTicks, $startedUtcTicks, 0, 1,
              'Jellyfin Web', 'device-1', 'A browser', 0,
              1, 1, ''
          )";

    private readonly ITestOutputHelper _output;
    private readonly string _root;

    public RetentionSweepTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first of the three conditions on issue #32: rows on both sides of
    /// the boundary, the task run over them, and only the older ones gone.
    /// </summary>
    [Fact]
    public async Task OnlyThePlaysOlderThanTheWindowAreDeleted()
    {
        var justInside = Now.UtcDateTime.AddDays(-Window).AddHours(1);
        var justOutside = Now.UtcDateTime.AddDays(-Window).AddHours(-1);

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(justOutside));
            store.Add(APlayStartedAt(justInside));
        }

        await ATask().ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);
        var left = after.AllPlays().ToList();

        Assert.Equal(new[] { justInside }, left.Select(play => play.StartedUtc));
    }

    /// <summary>
    /// A sweep with nothing old enough deletes nothing and still finishes. This
    /// is every run after the first on a server whose window has not moved, so
    /// it is the case that runs most often.
    /// </summary>
    [Fact]
    public async Task AStoreWithNothingOldEnoughKeepsEveryRow()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(Now.UtcDateTime.AddDays(-1)));
        }

        var reported = new RecordingProgress();
        await ATask().ExecuteAsync(reported, CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Single(after.AllPlays());
        Assert.Equal(new[] { 0d, 100d }, reported.Values);
    }

    /// <summary>
    /// The window is read at the run. An administrator who shortens a retention
    /// on the settings page gets the shorter one on the next sweep, with no
    /// restart in between, which is the shape issue #72 asks of every consumer.
    /// </summary>
    /// <remarks>
    /// The second run is handed a DIFFERENT object rather than the first one
    /// with a field moved, and that is what makes this bite. The server hands
    /// the plugin its whole configuration as one object, so a task that read
    /// one once and held it would still see a mutation of the object it was
    /// holding, and a case that only moved a field would pass over exactly the
    /// defect it is written against. Measured: holding the first reading in a
    /// field left this case green until it was written this way.
    /// </remarks>
    [Fact]
    public async Task TheWindowIsReadAtTheRunAndNotHeldFromBefore()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(Now.UtcDateTime.AddDays(-30)));
        }

        var configuration = new PluginConfiguration { PlayRowRetentionDays = Window };
        var task = ATask(() => configuration);

        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using (var untouched = new SqlitePlayStore(_root))
        {
            Assert.Single(untouched.AllPlays());
        }

        configuration = new PluginConfiguration { PlayRowRetentionDays = 7 };
        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);
        Assert.Empty(after.AllPlays());
    }


    /// <summary>
    /// The aggregate window on its own boundary. The rollup keyed on the first
    /// day that is kept stays, and the one keyed the day before it goes, so a
    /// cutoff that is one day out fails here rather than passing.
    /// </summary>
    /// <remarks>
    /// The play-row window is set wide on purpose. What is under test is which
    /// aggregates a sweep takes, and a run that also emptied the plays would
    /// leave a reader unable to say which of the two windows did it.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task OnlyTheAggregatesOlderThanTheirWindowAreDeleted()
    {
        var firstDayKept = DateOnly.FromDateTime(Now.UtcDateTime.AddDays(-AggregateWindow));

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(AtNoonOn(firstDayKept)));
            store.Add(APlayStartedAt(AtNoonOn(firstDayKept.AddDays(-1))));
        }

        var configuration = new PluginConfiguration
        {
            PlayRowRetentionDays = NoPlayRowGoes,
            DailyAggregateRetentionDays = AggregateWindow
        };

        await ATask(configuration).ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(new[] { firstDayKept }, after.AllRollups().Select(rollup => rollup.Day));
        Assert.Equal(2, after.AllPlays().Count());
    }

    /// <summary>
    /// The aggregate window is read at the run as well. An administrator who
    /// shortens it gets the shorter one on the next sweep with no restart in
    /// between, and the answer moves here because the setting moved rather than
    /// because the day did.
    /// </summary>
    /// <remarks>
    /// A different object on the second run, for the reason written at the
    /// play-row case above.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheAggregateWindowIsReadAtTheRunAndNotHeldFromBefore()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(Now.UtcDateTime.AddDays(-200)));
        }

        var configuration = new PluginConfiguration
        {
            PlayRowRetentionDays = NoPlayRowGoes,
            DailyAggregateRetentionDays = AggregateWindow
        };
        var task = ATask(() => configuration);

        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using (var untouched = new SqlitePlayStore(_root))
        {
            Assert.Single(untouched.AllRollups());
        }

        configuration = new PluginConfiguration
        {
            PlayRowRetentionDays = NoPlayRowGoes,
            DailyAggregateRetentionDays = 100
        };
        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.AllRollups());
        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// The aggregates go before the play rows, and a cancellation is where that
    /// order is visible. A run stopped between the two leaves the rows the
    /// deleted aggregates were folded from in the file, so a rebuild produces
    /// them again; the other order would have taken those rows away first and
    /// made the same deletion terminal.
    /// </summary>
    /// <remarks>
    /// The day here is past both windows, so a run that finished would end in
    /// the same state whichever loop went first. What separates the two orders
    /// is every moment before the end, which is why this case stops the sweep
    /// instead of letting it finish.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheAggregatesGoBeforeThePlayRowsTheyWereFoldedFrom()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(Now.UtcDateTime.AddDays(-500)));
        }

        var configuration = new PluginConfiguration
        {
            PlayRowRetentionDays = Window,
            DailyAggregateRetentionDays = AggregateWindow
        };

        using var stop = new CancellationTokenSource();
        var reported = new CancellingProgress(stop);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ATask(configuration, bite: 1).ExecuteAsync(reported, stop.Token));

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.AllRollups());
        Assert.Single(after.AllPlays());

        after.RebuildRollups();

        Assert.Single(after.AllRollups());
    }

    /// <summary>
    /// A store that has keyed no rollup states no zone for a day to be read in,
    /// and is not asked for one. A sweep reading the machine's zone instead
    /// would name a day such a store has never used, and would be asking for a
    /// deletion over days that mean nothing.
    /// </summary>
    [Fact]
    public void AStoreThatKeyedNoRollupIsNotAskedForADay()
    {
        var store = new AStoreThatKeyedNoRollup();

        var swept = new RetentionSweep(() => store, RetentionSweep.DefaultBite)
            .Run(Now.UtcDateTime, Now.UtcDateTime, new IgnoredProgress(), CancellationToken.None);

        Assert.Equal(0, swept.Rollups);
        Assert.Equal(0, swept.Plays);
        Assert.True(store.SpaceWasReclaimed);
    }
    /// <summary>
    /// The second of the three conditions: progress is reported as the sweep
    /// goes, so a large first sweep does not look hung.
    /// </summary>
    /// <remarks>
    /// One row per bite, so the reports are the sweep's own steps rather than
    /// one report at each end. What is asserted is that it starts at nothing,
    /// never goes backwards, never claims to be finished before the reclaim has
    /// run, and ends at a hundred.
    /// </remarks>
    [Fact]
    public async Task ASweepSaysHowFarThroughItIs()
    {
        SeedOldRows(10);

        var reported = new RecordingProgress();
        await ATask(bite: 1).ExecuteAsync(reported, CancellationToken.None);

        Assert.Equal(0d, reported.Values[0]);
        Assert.Equal(100d, reported.Values[^1]);
        Assert.Equal(12, reported.Values.Count);
        Assert.Equal(reported.Values, reported.Values.Order());
        Assert.All(reported.Values.Skip(1).SkipLast(1), value => Assert.InRange(value, 10d, 99d));
    }

    /// <summary>
    /// The other half of the second condition: a sweep can be stopped. What it
    /// has already deleted stays deleted, which is written into the method that
    /// does it rather than left for somebody to find out.
    /// </summary>
    [Fact]
    public async Task ASweepThatIsCancelledStopsAndLeavesTheRestWhereTheyAre()
    {
        SeedOldRows(10);

        using var stop = new CancellationTokenSource();
        var reported = new CancellingProgress(stop);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ATask(bite: 1).ExecuteAsync(reported, stop.Token));

        using var after = new SqlitePlayStore(_root);
        var left = after.AllPlays().Count();

        // One bite went before the first report, and the cancellation is
        // answered before the second. Anything else means the token was read
        // once at the start rather than between bites.
        Assert.Equal(9, left);
    }

    /// <summary>
    /// The third condition: the file on disk is smaller afterwards. A delete on
    /// its own does not do this, so removing the reclaim step turns this red,
    /// which is the whole of what it is here for. The two sizes are printed as
    /// well, because the issue asks for a measurement.
    /// </summary>
    [Fact]
    public async Task TheFileOnDiskIsSmallerAfterASweep()
    {
        SeedOldRows(5_000);
        SeedRowsStartedAt(Now.UtcDateTime.AddDays(-1), 500);

        var before = new FileInfo(Path.Combine(_root, SqlitePlayStore.FileName)).Length;

        await ATask().ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        var after = new FileInfo(Path.Combine(_root, SqlitePlayStore.FileName)).Length;

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "5000 rows past the window and 500 inside it: {0} bytes before the sweep, {1} bytes after.",
            before,
            after));

        Assert.True(after < before);
    }

    /// <summary>
    /// The server builds every scheduled task in a plugin assembly out of its
    /// own container, and fails the whole plugin over an argument it cannot
    /// resolve. So this builds the task the same way, out of a container the
    /// registrator filled, and a registration that stops working is caught here
    /// rather than by an administrator whose settings page has disappeared.
    /// </summary>
    [Fact]
    public void TheTaskIsBuiltTheWayTheServerBuildsIt()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        // ActivatorUtilities.CreateInstance is the call the server makes, and
        // nothing here opens a store or reaches for a plugin instance: the
        // store-opening function and the configuration function are both
        // handed on rather than run.
        var task = ActivatorUtilities.CreateInstance<RetentionSweepTask>(provider);

        Assert.NotEmpty(task.Name);
        Assert.NotEmpty(task.Key);
        Assert.NotEmpty(task.Description);
        Assert.NotEmpty(task.Category);

        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Equal(TimeSpan.FromHours(3).Ticks, trigger.TimeOfDayTicks);
    }

    /// <summary>
    /// A cutoff that is not in UTC is refused rather than stored as if it were.
    /// A local moment read as UTC moves the boundary by the machine's offset,
    /// which on this store means deleting rows that were inside the window.
    /// </summary>
    [Fact]
    public void ACutoffThatIsNotInUtcIsRefused()
    {
        using var store = new SqlitePlayStore(_root);
        var notUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => store.CountPlaysStartedBefore(notUtc));
        Assert.Throws<ArgumentException>(() => store.DeletePlaysStartedBefore(notUtc, DeletionClass.Retention, 1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static PlayRecord APlayStartedAt(DateTime startedUtc)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
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
    /// Noon on a day, in UTC, which is the zone these stores key their rollups
    /// in. Away from both midnights, so a case is about which day a rollup is
    /// keyed to rather than about which side of one a moment fell.
    /// </summary>
    /// <param name="day">The day.</param>
    /// <returns>Noon on it.</returns>
    private static DateTime AtNoonOn(DateOnly day) => day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);

    private RetentionSweepTask ATask(int bite = RetentionSweep.DefaultBite)
        => ATask(new PluginConfiguration { PlayRowRetentionDays = Window }, bite);

    private RetentionSweepTask ATask(PluginConfiguration configuration, int bite = RetentionSweep.DefaultBite)
        => ATask(() => configuration, bite);

    /// <summary>
    /// The task as the server builds it, reading its settings through a function
    /// rather than out of an object a case is holding.
    /// </summary>
    /// <param name="configuration">What the task reads its settings through, called once per run.</param>
    /// <param name="bite">How many rows one statement deletes.</param>
    /// <returns>The task.</returns>
    private RetentionSweepTask ATask(Func<PluginConfiguration> configuration, int bite = RetentionSweep.DefaultBite)
    {
        // A store per sweep, opened and closed by the sweep, which is what
        // happens on a server: the writer holds its own and this one is not it.
        return new RetentionSweepTask(
            new RetentionSweep(() => new SqlitePlayStore(_root), bite),
            new FixedClock(Now),
            configuration);
    }

    private void SeedOldRows(int rows)
        => SeedRowsStartedAt(Now.UtcDateTime.AddDays(-Window).AddDays(-1), rows);

    /// <summary>
    /// Puts rows in through one transaction rather than one write each. What is
    /// under test is which rows a cutoff reaches and how large the file is, and
    /// five thousand separate commits would measure the disk instead.
    /// </summary>
    /// <param name="startedUtc">The moment every seeded row starts at.</param>
    /// <param name="rows">How many to write.</param>
    private void SeedRowsStartedAt(DateTime startedUtc, int rows)
    {
        // Opening is migrating, so the store makes the file, the schema and the
        // indexes. It is disposed before the seeding connection opens, so there
        // is one writer at a time.
        using (var store = new SqlitePlayStore(_root))
        {
            _ = store.MostRecentPlays(1);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
            Pooling = false
        }.ToString());

        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertASeededPlay;

        command.Parameters.AddWithValue("$schemaVersion", SqlitePlayStore.SchemaVersion);
        command.Parameters.AddWithValue("$startedUtcTicks", startedUtc.Ticks);
        var userId = command.Parameters.Add("$userId", SqliteType.Text);
        var itemId = command.Parameters.Add("$itemId", SqliteType.Text);

        for (var row = 0; row < rows; row++)
        {
            userId.Value = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            itemId.Value = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
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
    /// Keeps every value the sweep reported, in order.
    /// </summary>
    private sealed class RecordingProgress : IProgress<double>
    {
        private readonly List<double> _values = new();

        public IReadOnlyList<double> Values => _values;

        public void Report(double value) => _values.Add(value);
    }


    /// <summary>
    /// A store holding no rollup, and therefore stating no zone one could be
    /// keyed in.
    /// </summary>
    /// <remarks>
    /// Every rollup call refuses rather than answering with nothing. A store
    /// that answered zero would let a sweep asking about days it has no zone
    /// for pass through this case, which is the whole of what it is here to
    /// catch.
    /// </remarks>
    private sealed class AStoreThatKeyedNoRollup : IPlayStore
    {
        public bool SpaceWasReclaimed { get; private set; }

        public TimeZoneInfo? RollupZone => null;

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => 0;

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit) => 0;

        public void ReclaimFreedSpace() => SpaceWasReclaimed = true;

        public void Dispose()
        {
        }

        public long CountRollupsBefore(DateOnly day) => throw NotPartOfThis();

        public int DeleteRollupsBefore(DateOnly day, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public void Add(PlayRecord play) => throw NotPartOfThis();

        public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

        public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

        public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        public IEnumerable<DailyRollup> AllRollups() => throw NotPartOfThis();

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit) => throw NotPartOfThis();

        public void RebuildRollups() => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => throw NotPartOfThis();

        public ConsentRecord? ConsentFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithConsent() => throw NotPartOfThis();

        public void RecordConsent(ConsentRecord consent) => throw NotPartOfThis();

        public void ForgetConsentFor(Guid userId) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("This store has keyed no rollup and answers nothing about days.");
    }

    /// <summary>
    /// Cancels the sweep the first time it says it has deleted something.
    /// </summary>
    /// <remarks>
    /// Not on the opening report of nothing, which arrives before the first
    /// bite. Cancelling there would prove that the token is read at the start
    /// and say nothing about whether it is read between bites, which is the
    /// whole of what makes a long sweep stoppable.
    /// </remarks>
    private sealed class CancellingProgress : IProgress<double>
    {
        private readonly CancellationTokenSource _stop;

        public CancellingProgress(CancellationTokenSource stop)
        {
            _stop = stop;
        }

        public void Report(double value)
        {
            if (value > 0)
            {
                _stop.Cancel();
            }
        }
    }
}
