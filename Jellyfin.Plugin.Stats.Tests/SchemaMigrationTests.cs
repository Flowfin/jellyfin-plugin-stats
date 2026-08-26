// What an upgrade does to the rows already in a store.
//
// The runner is driven twice over. Once over a step list this file makes up,
// which is where "each earlier version" can actually mean more than one, and
// once over the list the plugin ships, which is what a real store meets. A
// vocabulary a test invents proves the runner; the real list proves what this
// build does with it, and neither proves the other.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class SchemaMigrationTests : IDisposable
{
    private const string CreateTheFixtureTable =
        "CREATE TABLE IF NOT EXISTS listens (Id INTEGER PRIMARY KEY, Minutes INTEGER NOT NULL)";

    private const string AddTheSecondsColumn =
        "ALTER TABLE listens ADD COLUMN Seconds INTEGER NULL";

    private const string FillTheSecondsColumn =
        "UPDATE listens SET Seconds = Minutes * 60";

    private const string AddTheLabelColumn =
        "ALTER TABLE listens ADD COLUMN Label TEXT NULL";

    private readonly string _root;

    public SchemaMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// A store at each earlier version, migrated to the newest one, with the
    /// rows it had before still there and readable afterwards. The second step
    /// derives a column from one that was already there, so a step that threw
    /// the table away and built it again would leave the derived column empty
    /// even if it happened to leave the row count right.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RowsSurviveAnUpgradeFromEveryEarlierVersion(int startAt)
    {
        using var connection = OpenAFile("fixture.db");

        // Take the store up to the version this case starts at and put rows in
        // it the way the build at that version would have written them. Version
        // one has no seconds column at all and version two has one its own code
        // fills, so a case writing both through one statement would be testing a
        // shape no installation ever had. Version zero is a file nothing has run
        // against yet and has no table to put a row in.
        if (startAt == 1)
        {
            SchemaMigrator.MigrateToLatest(connection, Fixture(1));
            Insert(connection, 1, 3);
            Insert(connection, 2, 7);
        }
        else if (startAt == 2)
        {
            SchemaMigrator.MigrateToLatest(connection, Fixture(2));
            InsertWithSeconds(connection, 1, 3, 180);
            InsertWithSeconds(connection, 2, 7, 420);
        }

        var reached = SchemaMigrator.MigrateToLatest(connection, Fixture(3));

        Assert.Equal(3, reached);
        if (startAt > 0)
        {
            Assert.Equal(new[] { (1, 3, 180), (2, 7, 420) }, ReadListens(connection));
        }
        else
        {
            Assert.Empty(ReadListens(connection));
        }
    }

    /// <summary>
    /// Running the steps again changes nothing. A server restarts, and a
    /// migration that ran a second time would run every statement over rows it
    /// had already moved.
    /// </summary>
    [Fact]
    public void RunningTheStepsASecondTimeIsANoOp()
    {
        using var connection = OpenAFile("fixture.db");

        SchemaMigrator.MigrateToLatest(connection, Fixture(3));
        InsertWithSeconds(connection, 1, 3, 180);

        Assert.Equal(3, SchemaMigrator.MigrateToLatest(connection, Fixture(3)));
        Assert.Equal(new[] { (1, 3, 180) }, ReadListens(connection));
    }

    /// <summary>
    /// A store written by a later build is refused, and the message names both
    /// numbers, because the answer an administrator needs is which build to put
    /// back.
    /// </summary>
    [Fact]
    public void AStoreFromALaterBuildIsRefusedAndBothVersionsAreNamed()
    {
        using var connection = OpenAFile("fixture.db");
        SchemaMigrator.MigrateToLatest(connection, Fixture(3));

        var refusal = Assert.Throws<StoreIsNewerThanThePluginException>(
            () => SchemaMigrator.MigrateToLatest(connection, Fixture(2)));

        Assert.Equal(3, refusal.StoreVersion);
        Assert.Equal(2, refusal.PluginVersion);
        Assert.Contains("3", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("2", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal leaves the rows where they are. A downgrade that emptied the
    /// store on its way out would be the failure this whole issue is about,
    /// arriving through the one path that was supposed to prevent it.
    /// </summary>
    [Fact]
    public void ARefusedStoreStillHasItsRows()
    {
        using var connection = OpenAFile("fixture.db");
        SchemaMigrator.MigrateToLatest(connection, Fixture(3));
        InsertWithSeconds(connection, 1, 3, 180);

        Assert.Throws<StoreIsNewerThanThePluginException>(
            () => SchemaMigrator.MigrateToLatest(connection, Fixture(2)));

        Assert.Equal(new[] { (1, 3, 180) }, ReadListens(connection));
    }

    /// <summary>
    /// A store the plugin itself created reports the version the plugin ships,
    /// and opening it again neither re-runs a step nor moves the number.
    /// </summary>
    [Fact]
    public void ThePluginsOwnStoreReportsTheVersionThisBuildShips()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            Assert.NotNull(store);
        }

        using var connection = OpenAFile(SqlitePlayStore.FileName);

        Assert.Equal(SchemaMigrations.Latest, SchemaMigrator.CurrentVersion(connection));
    }

    /// <summary>
    /// The build before this one wrote the plays table and no version at all.
    /// Such a store arrives reading as version zero with its rows in it, and the
    /// first step is conditional, so the upgrade records the version it was
    /// already at and leaves every row alone.
    /// </summary>
    /// <summary>
    /// A store from before the delivery method was named for its moment keeps
    /// the value it had, under the new name, and says nothing about a change it
    /// was never watching for.
    /// </summary>
    /// <remarks>
    /// The step renames a column and adds one, and neither reads a row, so what
    /// this case is really about is that an installation arrives at the newest
    /// shape without anything being rewritten. A step that rebuilt the table
    /// instead would pass every other case here and lose the rows.
    /// </remarks>
    [Fact]
    public void AStoreFromBeforeTheMethodWasNamedForItsMomentKeepsItsValue()
    {
        Directory.CreateDirectory(_root);

        using (var connection = OpenAFile(SqlitePlayStore.FileName))
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 3).ToList());

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethod,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      3, $userId, $itemId, 'Movie', 'An older film',
                      $started, $ended, 0, 0,
                      'Web', 'a-device', 'A device', 3,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$started", new DateTime(2026, 1, 2, 20, 0, 0, DateTimeKind.Utc).Ticks);
            insert.Parameters.AddWithValue("$ended", new DateTime(2026, 1, 2, 20, 41, 0, DateTimeKind.Utc).Ticks);
            insert.ExecuteNonQuery();
        }

        using var reopened = new SqlitePlayStore(_root);

        var read = Assert.Single(reopened.MostRecentPlays(10));

        Assert.Equal("An older film", read.ItemName);
        Assert.Equal(PlayMethod.Transcode, read.PlayMethodAtStart);
        Assert.Null(read.PlayMethodChangedUtc);
    }

    [Fact]
    public void AStoreWrittenBeforeThereWasAVersionKeepsItsRows()
    {
        // The state that build left, built by running the one step it knew and
        // then taking the version away, rather than by taking the version away
        // from a store at the newest shape. The second is a state no build ever
        // wrote: it has every later step's work already done and says it has
        // had none, so replaying the steps over it is not the upgrade this case
        // is about.
        Directory.CreateDirectory(_root);

        using (var connection = OpenAFile(SqlitePlayStore.FileName))
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 1).ToList());

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethod,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      1, $userId, $itemId, 'Episode', 'An episode',
                      $started, $ended, 0, 0,
                      'Web', 'a-device', 'A device', 0,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.NewGuid().ToString("N"));
            insert.Parameters.AddWithValue("$started", new DateTime(2026, 1, 2, 20, 0, 0, DateTimeKind.Utc).Ticks);
            insert.Parameters.AddWithValue("$ended", new DateTime(2026, 1, 2, 20, 41, 0, DateTimeKind.Utc).Ticks);
            insert.ExecuteNonQuery();

            using var drop = connection.CreateCommand();
            drop.CommandText = "DELETE FROM schema_version";
            drop.ExecuteNonQuery();
        }

        using var reopened = new SqlitePlayStore(_root);

        var read = Assert.Single(reopened.MostRecentPlays(10));
        Assert.Equal("An episode", read.ItemName);

        using var after = OpenAFile(SqlitePlayStore.FileName);
        Assert.Equal(SchemaMigrations.Latest, SchemaMigrator.CurrentVersion(after));
    }

    [Fact]
    public void TheRunnerRefusesToBeCalledOnNothing()
    {
        using var connection = OpenAFile("fixture.db");

        Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateToLatest(null!, Fixture(1)));
        Assert.Throws<ArgumentNullException>(() => SchemaMigrator.MigrateToLatest(connection, null!));
        Assert.Throws<ArgumentNullException>(() => SchemaMigrator.CurrentVersion(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A step list this file owns, cut off at the version asked for.
    /// </summary>
    /// <remarks>
    /// Three steps rather than two, because a runner that applied only the last
    /// outstanding step would pass a list of two and fail a list of three, and
    /// the whole point of the list is that a store two versions behind arrives
    /// somewhere correct.
    /// </remarks>
    /// <param name="upTo">The last version to include.</param>
    /// <returns>The steps.</returns>
    private static IReadOnlyList<SchemaMigration> Fixture(int upTo)
    {
        var all = new List<SchemaMigration>
        {
            new() { Version = 1, Statements = [CreateTheFixtureTable] },
            new() { Version = 2, Statements = [AddTheSecondsColumn, FillTheSecondsColumn] },
            new() { Version = 3, Statements = [AddTheLabelColumn] }
        };

        return all.GetRange(0, upTo);
    }

    /// <summary>
    /// A row the way version one of the fixture schema wrote them.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="id">The row id.</param>
    /// <param name="minutes">The value the second step derives from.</param>
    private static void Insert(SqliteConnection connection, int id, int minutes)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO listens (Id, Minutes) VALUES ($id, $minutes)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$minutes", minutes);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A row the way version two and later wrote them, filling the column the
    /// second step added for the rows that were already there.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="id">The row id.</param>
    /// <param name="minutes">The value the second step derives from.</param>
    /// <param name="seconds">The derived value, written rather than derived.</param>
    private static void InsertWithSeconds(SqliteConnection connection, int id, int minutes, int seconds)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO listens (Id, Minutes, Seconds) VALUES ($id, $minutes, $seconds)";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$minutes", minutes);
        command.Parameters.AddWithValue("$seconds", seconds);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Reads the fixture rows with the column the second step derives, so a row
    /// that survived in name only is not mistaken for one that survived.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <returns>Every row, as id, minutes and seconds.</returns>
    private static List<(int Id, int Minutes, int Seconds)> ReadListens(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Minutes, Seconds FROM listens ORDER BY Id";

        var rows = new List<(int, int, int)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return rows;
    }

    private SqliteConnection OpenAFile(string name)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, name),
            Pooling = false
        }.ToString());

        connection.Open();
        return connection;
    }

    private static PlayRecord APlay()
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
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
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
                Reasons = Array.Empty<string>()
            }
        };
    }
}
