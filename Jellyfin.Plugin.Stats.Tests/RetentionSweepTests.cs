// The retention sweep, driven over a temporary directory. Nothing here needs a
// server, a socket or a service: the store takes the folder it writes into as an
// argument and the sweep takes the moment it measures back from, so a test hands
// it both and the boundary is a value rather than the day the suite ran.
//
// The moment every case is written around is fixed. A row is placed one hour on
// each side of the cutoff a ninety day window produces from it, which is the
// case a sweep that was one day out would still pass and one that read the
// machine clock could not be written at all.

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
    [Fact]
    public async Task TheWindowIsReadAtTheRunAndNotHeldFromBefore()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(Now.UtcDateTime.AddDays(-30)));
        }

        var configuration = new PluginConfiguration { PlayRowRetentionDays = Window };
        var task = ATask(configuration);

        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using (var untouched = new SqlitePlayStore(_root))
        {
            Assert.Single(untouched.AllPlays());
        }

        configuration.PlayRowRetentionDays = 7;
        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);
        Assert.Empty(after.AllPlays());
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
        Assert.Throws<ArgumentException>(() => store.DeletePlaysStartedBefore(notUtc, 1));
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

    private RetentionSweepTask ATask(int bite = RetentionSweep.DefaultBite)
        => ATask(new PluginConfiguration { PlayRowRetentionDays = Window }, bite);

    private RetentionSweepTask ATask(PluginConfiguration configuration, int bite = RetentionSweep.DefaultBite)
    {
        // A store per sweep, opened and closed by the sweep, which is what
        // happens on a server: the writer holds its own and this one is not it.
        return new RetentionSweepTask(
            new RetentionSweep(() => new SqlitePlayStore(_root), bite),
            new FixedClock(Now),
            () => configuration);
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
