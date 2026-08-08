// The index set, one test per index, each naming the query it serves.
//
// A store seeded with a year of plays is what makes these tests mean anything.
// SQLite picks a scan over an index on a table it can hold in a page or two, so
// a plan asserted over three rows would be asserting the planner's warm-up
// rather than the index, and would keep passing after the index was deleted.
//
// The queries here are the four shapes issue #30 names, and they are owned by
// this file rather than by the plugin because the reports that will run them do
// not exist yet. That is the bound on what these tests prove: that the index set
// serves these shapes, not that the reports are written in them. Whoever writes
// the query surface in #51 either writes these shapes or comes back here.
//
// The last test is the half that keeps the set from growing quietly: an index on
// the plays table that no test below names is a failure, so an index that no
// query uses cannot survive by nobody looking.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Jellyfin.Plugin.Stats.Data;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class PlayStoreIndexTests : IDisposable
{
    // A year, at fifty plays a day. Small enough that the suite does not notice
    // it and large enough that the planner stops preferring a scan.
    private const int Days = 365;
    private const int PlaysPerDay = 50;
    private const int Users = 12;
    private const int Items = 900;

    private const string ExplainPrefix = "EXPLAIN QUERY PLAN ";

    // Only the columns that cannot be absent, plus the four the queries below
    // read. The rest are left to their nulls: what is under test is which rows
    // the planner reaches, and a row's optional half does not move that.
    private const string InsertASeededPlay =
        @"INSERT INTO plays (
              SchemaVersion, UserId, ItemId, ItemType, ItemName,
              StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
              ClientName, DeviceId, DeviceName, PlayMethod,
              TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
          ) VALUES (
              $schemaVersion, $userId, $itemId, $itemType, $itemName,
              $startedUtcTicks, $endedUtcTicks, $watchedDurationTicks, 1,
              'Web', 'a-device', 'A device', 0,
              1, 1, ''
          )";

    // Everything the dashboard shows over a window, for everybody.
    private const string PlaysInAWindow =
        @"SELECT Id FROM plays
          WHERE StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY StartedUtcTicks";

    // The page a signed-in user opens about themselves, and the yearly wrap-up.
    private const string PlaysForOneUserInAWindow =
        @"SELECT Id FROM plays
          WHERE UserId = $userId AND StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY StartedUtcTicks";

    // One item's history, which is what a top-items list drills into.
    private const string PlaysOfOneItemInAWindow =
        @"SELECT Id FROM plays
          WHERE ItemId = $itemId AND StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY StartedUtcTicks";

    // Films against episodes against everything else, over a window.
    private const string PlaysOfOneItemTypeInAWindow =
        @"SELECT Id FROM plays
          WHERE ItemType = $itemType AND StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY StartedUtcTicks";

    private const string IndexesOnThePlaysTable =
        @"SELECT name FROM sqlite_master
          WHERE type = 'index' AND tbl_name = 'plays' AND name NOT LIKE 'sqlite_autoindex%'
          ORDER BY name";

    private const string CountThePlays = "SELECT COUNT(*) FROM plays";

    // A fixed moment rather than the machine clock. A test that reads the clock
    // behaves differently in June and on the last day of December, which is the
    // defect the ambient clock rule refuses in the plugin and in the suite.
    private static readonly DateTime Epoch = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ITestOutputHelper _output;
    private readonly string _root;

    public PlayStoreIndexTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Served by ix_plays_started: everything played in a window, which is what
    /// every server-wide report over a period reads.
    /// </summary>
    [Fact]
    public void AWindowOfPlaysIsServedByTheStartIndex()
    {
        using var connection = ASeededStore();

        AssertPlanUsesIndex(connection, PlaysInAWindow, "ix_plays_started", AWindow);
    }

    /// <summary>
    /// Served by ix_plays_user_started: one user over a window, which is the
    /// page a user opens about themselves and the whole of the wrap-up.
    /// </summary>
    [Fact]
    public void OneUsersWindowIsServedByTheUserAndStartIndex()
    {
        using var connection = ASeededStore();

        AssertPlanUsesIndex(
            connection,
            PlaysForOneUserInAWindow,
            "ix_plays_user_started",
            command =>
            {
                AWindow(command);
                command.Parameters.AddWithValue("$userId", Text(UserAt(3)));
            });
    }

    /// <summary>
    /// Served by ix_plays_item_started: one item over a window, which is what a
    /// top-items list drills into.
    /// </summary>
    [Fact]
    public void OneItemsWindowIsServedByTheItemAndStartIndex()
    {
        using var connection = ASeededStore();

        AssertPlanUsesIndex(
            connection,
            PlaysOfOneItemInAWindow,
            "ix_plays_item_started",
            command =>
            {
                AWindow(command);
                command.Parameters.AddWithValue("$itemId", Text(ItemAt(17)));
            });
    }

    /// <summary>
    /// Served by ix_plays_item_type_started: one item type over a window, which
    /// is how films are told from episodes in every breakdown.
    /// </summary>
    [Fact]
    public void OneItemTypesWindowIsServedByTheItemTypeAndStartIndex()
    {
        using var connection = ASeededStore();

        AssertPlanUsesIndex(
            connection,
            PlaysOfOneItemTypeInAWindow,
            "ix_plays_item_type_started",
            command =>
            {
                AWindow(command);
                command.Parameters.AddWithValue("$itemType", "Movie");
            });
    }

    /// <summary>
    /// Every index on the table is one of the four above. An index costs a write
    /// on every insert and a rebuild on every upgrade, so one that no query
    /// reaches is a cost with nothing on the other side, and this is what stops
    /// one being added and forgotten.
    /// </summary>
    [Fact]
    public void TheTableCarriesNoIndexThatNoQueryHereNames()
    {
        using var connection = ASeededStore();

        Assert.Equal(
            new[]
            {
                "ix_plays_item_started",
                "ix_plays_item_type_started",
                "ix_plays_started",
                "ix_plays_user_started"
            },
            IndexNames(connection));
    }

    /// <summary>
    /// A store written by the build before this one carries the rows and none of
    /// the indexes. Opening it here builds them over the rows that are already
    /// in it, and the rows stay where they are.
    /// </summary>
    [Fact]
    public void AStoreFromTheBuildBeforeTheIndexesGetsThemAndKeepsItsRows()
    {
        Directory.CreateDirectory(_root);

        using (var before = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
            Pooling = false
        }.ToString()))
        {
            before.Open();

            // The step list as it was one version ago, which is the first entry
            // of the real one rather than a list this file made up.
            SchemaMigrator.MigrateToLatest(before, [SchemaMigrations.All[0]]);
            Seed(before);

            Assert.Empty(IndexNames(before));
        }

        using (var store = new SqlitePlayStore(_root))
        {
            Assert.Equal(10, store.MostRecentPlays(10).Count);
        }

        using var after = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
            Pooling = false
        }.ToString());

        after.Open();

        Assert.Equal(4, IndexNames(after).Count);
        Assert.Equal(Days * PlaysPerDay, RowCount(after));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static IReadOnlyList<string> IndexNames(SqliteConnection connection)
    {
        var found = new List<string>();

        using var command = connection.CreateCommand();
        command.CommandText = IndexesOnThePlaysTable;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }

    private static long RowCount(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = CountThePlays;

        return (long)command.ExecuteScalar()!;
    }

    // The store writes a Guid as thirty-two digits with no dashes, and reads
    // it back the same way. A seed row or a bound parameter in any other spelling
    // matches nothing, and a query that matches nothing is a query whose plan and
    // whose timing both mean something else.
    private static string Text(Guid value) => value.ToString("N", CultureInfo.InvariantCulture);

    private static Guid UserAt(int index)
    {
        return new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
    }

    private static Guid ItemAt(int index)
    {
        return new Guid(index, 0, 0, [0, 0, 0, 0, 0, 0, 0, 2]);
    }

    private static void AWindow(SqliteCommand command)
    {
        // The middle third of the year, so the window is a range the planner has
        // to narrow to rather than the whole table under another name.
        command.Parameters.AddWithValue("$from", Epoch.AddDays(120).Ticks);
        command.Parameters.AddWithValue("$to", Epoch.AddDays(240).Ticks);
    }

    private void AssertPlanUsesIndex(
        SqliteConnection connection,
        string query,
        string index,
        Action<SqliteCommand> bind)
    {
        var plan = new StringBuilder();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = ExplainPrefix + query;
            bind(command);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                plan.AppendLine(reader.GetString(reader.GetOrdinal("detail")));
            }
        }

        var elapsed = TimeOne(connection, query, bind);

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}\n{1}rows read in {2:F1} ms",
            query.Trim(),
            plan.ToString(),
            elapsed.TotalMilliseconds));

        Assert.Contains(index, plan.ToString(), StringComparison.Ordinal);
    }

    private TimeSpan TimeOne(SqliteConnection connection, string query, Action<SqliteCommand> bind)
    {
        // Once to warm the pages, then the run that is reported. A first read of
        // a file nobody has touched measures the disk rather than the plan.
        for (var run = 0; run < 2; run++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;
            bind(command);

            var watch = Stopwatch.StartNew();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    // Reading every row is the point: a plan that finds the
                    // first row quickly and then scans is not a served query.
                }
            }

            watch.Stop();
            if (run == 1)
            {
                return watch.Elapsed;
            }
        }

        return TimeSpan.Zero;
    }

    private SqliteConnection ASeededStore()
    {
        // The store creates the file, the schema and the indexes, because
        // opening is migrating. It is disposed before the seeding connection
        // opens so there is one writer at a time.
        using (var store = new SqlitePlayStore(_root))
        {
            _ = store.MostRecentPlays(1);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
            Pooling = false
        }.ToString());

        connection.Open();
        Seed(connection);
        return connection;
    }

    private void Seed(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertASeededPlay;

        var schemaVersion = command.Parameters.Add("$schemaVersion", SqliteType.Integer);
        var userId = command.Parameters.Add("$userId", SqliteType.Text);
        var itemId = command.Parameters.Add("$itemId", SqliteType.Text);
        var itemType = command.Parameters.Add("$itemType", SqliteType.Text);
        var itemName = command.Parameters.Add("$itemName", SqliteType.Text);
        var startedUtcTicks = command.Parameters.Add("$startedUtcTicks", SqliteType.Integer);
        var endedUtcTicks = command.Parameters.Add("$endedUtcTicks", SqliteType.Integer);
        var watchedDurationTicks = command.Parameters.Add("$watchedDurationTicks", SqliteType.Integer);

        for (var day = 0; day < Days; day++)
        {
            for (var play = 0; play < PlaysPerDay; play++)
            {
                var index = (day * PlaysPerDay) + play;
                var started = Epoch.AddDays(day).AddMinutes(play * 20);

                schemaVersion.Value = SqlitePlayStore.SchemaVersion;
                userId.Value = Text(UserAt(index % Users));
                itemId.Value = Text(ItemAt(index % Items));
                itemType.Value = (index % 3) == 0 ? "Movie" : "Episode";
                itemName.Value = string.Format(CultureInfo.InvariantCulture, "Item {0}", index % Items);
                startedUtcTicks.Value = started.Ticks;
                endedUtcTicks.Value = started.AddMinutes(45).Ticks;
                watchedDurationTicks.Value = TimeSpan.FromMinutes(45).Ticks;

                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }
}
