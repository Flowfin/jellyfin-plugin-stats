// What happens on a server when the store on disk was written by a later build
// of this plugin.
//
// The refusal itself is proved next door, in SchemaMigrationTests: the migrator
// throws, both version numbers are in the message, and the rows are still there
// afterwards. What that cannot say is what the refusal does to the server,
// because a refusal is only as safe as the caller that meets it, and the caller
// is the write path.
//
// So this file drives the whole route rather than the migrator alone. A real
// store file is taken past the version this build knows, and then handed to the
// queue the container assembles, which is the only thing in the plugin that
// opens one. The headless policy refuses running a real server to watch it stay
// up, so what is asserted instead is the property a server would depend on: the
// exception is met on the writer's own thread, counted and reported, and
// nothing is thrown back at whoever handed the play over. Issue #28.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class StoreFromALaterBuildTests : IDisposable
{
    private const string CountThePlays = "SELECT COUNT(*) FROM plays";

    private const string ForgetTheVersion = "DELETE FROM schema_version";

    private const string RecordTheVersion = "INSERT INTO schema_version (Version) VALUES ($version)";

    private readonly string _root;

    public StoreFromALaterBuildTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// A play finished while the store on disk is from a later build. The row is
    /// lost and counted, one line says why, and the call that handed the play
    /// over returns as it does on any other day.
    /// </summary>
    /// <remarks>
    /// The count is what makes this more than the general case beside it in
    /// QueuedPlayWriterTests, which drives a folder that cannot be read at all.
    /// A store from a later build is the one failure where the file is
    /// perfectly readable and the plugin is the thing that is out of date, and
    /// it is the case where emptying the store would look like a repair.
    /// </remarks>
    [Fact]
    public void APlayFinishedAgainstALaterStoreIsCountedAndNothingIsThrownBack()
    {
        RowsAlreadyInTheStore(1);
        MoveTheStorePastThisBuild();

        var logger = new RecordingLogger<QueuedPlayWriter>();
        using var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(_root),
            QueuedPlayWriter.DefaultBound,
            logger);

        // Neither of these may throw. Add is reached from inside the server's
        // own event dispatch and Dispose from its shutdown, so an exception out
        // of either is this plugin's failure arriving in the server's stack.
        writer.Add(APlay());
        writer.Dispose();

        Assert.Equal(1, writer.Accepted);
        Assert.Equal(1, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.Single(logger.Lines);
        Assert.Contains(
            nameof(StoreIsNewerThanThePluginException),
            logger.Lines[0].Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The rows the later build wrote are still there when the older plugin has
    /// given up on them. A downgrade that emptied the store on its way past is
    /// the failure issue #28 exists against, and the write path is the second
    /// place it could arrive through.
    /// </summary>
    [Fact]
    public void TheRowsTheLaterBuildWroteAreStillThereAfterwards()
    {
        RowsAlreadyInTheStore(3);
        MoveTheStorePastThisBuild();

        using (var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(_root),
            QueuedPlayWriter.DefaultBound,
            new RecordingLogger<QueuedPlayWriter>()))
        {
            writer.Add(APlay());
        }

        Assert.Equal(3, PlaysOnDisk());
    }

    /// <summary>
    /// The refusal names the version on disk and the version this build knows,
    /// through the route a server takes rather than through the migrator
    /// directly.
    /// </summary>
    [Fact]
    public void OpeningTheStoreNamesBothVersions()
    {
        RowsAlreadyInTheStore(1);
        MoveTheStorePastThisBuild();

        var refusal = Assert.Throws<StoreIsNewerThanThePluginException>(() => new SqlitePlayStore(_root));

        Assert.Equal(SchemaMigrations.Latest + 1, refusal.StoreVersion);
        Assert.Equal(SchemaMigrations.Latest, refusal.PluginVersion);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A store this build wrote, with rows in it.
    /// </summary>
    /// <param name="count">How many rows.</param>
    private void RowsAlreadyInTheStore(int count)
    {
        using var store = new SqlitePlayStore(_root);

        for (var i = 0; i < count; i++)
        {
            store.Add(APlay());
        }
    }

    /// <summary>
    /// Stamps the store one version past the last step this build ships, which
    /// is what a store written by a later plugin looks like to this one.
    /// </summary>
    private void MoveTheStorePastThisBuild()
    {
        using var connection = OpenTheFile();

        Execute(connection, ForgetTheVersion, version: null);
        Execute(connection, RecordTheVersion, SchemaMigrations.Latest + 1);
    }

    /// <summary>
    /// How many rows the file holds, read without going through the store,
    /// which this build can no longer open.
    /// </summary>
    /// <returns>The count.</returns>
    private long PlaysOnDisk()
    {
        using var connection = OpenTheFile();
        using var command = connection.CreateCommand();
        command.CommandText = CountThePlays;

        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// Opens the store file directly.
    /// </summary>
    /// <returns>The connection.</returns>
    private SqliteConnection OpenTheFile()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
            Pooling = false
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, int? version)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (version is not null)
        {
            command.Parameters.AddWithValue("$version", version.Value);
        }

        command.ExecuteNonQuery();
    }

    private static PlayRecord APlay()
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.Transcode,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = true,
                PeakBitrate = 8_000_000,
                TypicalBitrate = 6_000_000,
                HardwareAcceleration = "qsv",
                Reasons = ["VideoCodecNotSupported", "AudioBitrateNotSupported"]
            }
        };
    }
}
