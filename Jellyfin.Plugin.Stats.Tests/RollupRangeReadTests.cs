// The read a report over a year issues, and the plan behind it. Issue #254.
//
// The table has been walked from end to end since #252 and read no other way,
// which is the shape an export has and not the shape a report has. Reading one
// account's year through that walk touches every day of every account on the
// server to answer about one account's twelve months, which is the scan the fold
// on the write path exists to stop.
//
// Two things are asserted here and they need different stores. Which rows come
// back is asserted over a handful of plays, because the answer has to be
// readable in the case. Which rows the planner reaches is asserted over a store
// with years of days in it, because SQLite prefers a scan on a table it can hold
// in a page or two - a plan asserted over three rows would be asserting the
// planner's warm-up and would keep passing after the key it names was gone.
//
// Nothing here reads a clock or a setting. Berlin is the zone throughout, since
// the days a rollup is keyed by are local days, and a case that fell back to UTC
// would come out a day off on the plays placed either side of midnight rather
// than passing quietly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.Stats.Data;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class RollupRangeReadTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private static readonly Guid Bob = new("22222222222222222222222222222222");

    // Three years of days for a dozen accounts, written straight into the table
    // rather than through the fold. What is under test at that size is which
    // rows the planner reaches, and a play row that produced a rollup and a
    // rollup written directly are the same row to a planner.
    private const int SeededDays = 365 * 3;

    private const int SeededAccounts = 12;

    private const string ExplainPrefix = "EXPLAIN QUERY PLAN ";

    private readonly ITestOutputHelper _output;

    private readonly string _root;

    public RollupRangeReadTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// The first condition of issue #254 as far as the store reaches it: a year
    /// is read as a range over the days it was folded into, and the planner
    /// walks those days rather than the table.
    /// </summary>
    /// <remarks>
    /// The plan is asserted over a store holding three years of days for a dozen
    /// accounts. The statement is read out of the source rather than copied, so
    /// a rewrite of the read is measured instead of a copy of what it used to
    /// say, which is the shape <see cref="PlayStoreIndexTests"/> already uses for
    /// the one statement it does not own.
    /// </remarks>
    [Fact]
    public void AYearIsSearchedOverTheDaysAndNeverScannedOverTheTable()
    {
        Directory.CreateDirectory(_root);

        using var connection = OpenTheFile();
        SchemaMigrator.MigrateToLatest(connection, SchemaMigrations.All);
        SeedRollups(connection);

        var plan = PlanOf(
            connection,
            TheRangeStatement(),
            command =>
            {
                command.Parameters.AddWithValue("$from", "2026-01-01");
                command.Parameters.AddWithValue("$to", "2027-01-01");
                command.Parameters.AddWithValue("$userId", Ada.ToString("N", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$limit", 10_000);
            });

        _output.WriteLine(plan);

        Assert.Contains("SEARCH", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("SCAN", plan, StringComparison.Ordinal);

        // The days are what is searched by. A plan that reached the rows some
        // other way would still say SEARCH, so the bound the range puts on the
        // walk is named rather than assumed.
        Assert.Contains("Day>", plan, StringComparison.Ordinal);
        Assert.Contains("Day<", plan, StringComparison.Ordinal);

        // Nothing is sorted afterwards. The statement asks for the key's own
        // order with the account taken out of it, and a plan carrying a sort
        // would mean every row of the range is held before the first one is
        // handed back.
        Assert.DoesNotContain("TEMP B-TREE", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// The walk that already exists reaches the same rows by reading all of
    /// them, which is what the range read is here instead of.
    /// </summary>
    /// <remarks>
    /// Asserted rather than argued. Without this the case above proves the range
    /// read is served by the key and says nothing about what it saved, so a
    /// later change that made the two statements equivalent would leave the pair
    /// of them looking as though a choice was still being made.
    /// </remarks>
    [Fact]
    public void TheWalkThisReplacesReadsTheWholeTable()
    {
        Directory.CreateDirectory(_root);

        using var connection = OpenTheFile();
        SchemaMigrator.MigrateToLatest(connection, SchemaMigrations.All);
        SeedRollups(connection);

        var plan = PlanOf(connection, TheWalkStatement(), _ => { });

        _output.WriteLine(plan);

        Assert.Contains("SCAN", plan, StringComparison.Ordinal);
    }

    /// <summary>
    /// The range is half-open and the account is the caller's own: the first day
    /// is in, the day the range ends on is out, and nobody else's rows come
    /// back.
    /// </summary>
    /// <remarks>
    /// Both ends and both accounts in one case, because the three mistakes
    /// available here produce answers that look correct on their own. A range
    /// that included its last day gives a year thirteen months long only when
    /// something was played on the first of January; one that excluded its first
    /// day loses a day nobody would miss; and one that answered for everybody
    /// gives a wrap-up that is somebody else's and reads perfectly.
    /// </remarks>
    [Fact]
    public void OnlyTheAccountsOwnDaysInsideTheRangeComeBack()
    {
        Directory.CreateDirectory(_root);

        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            // Local days in Berlin, which is what the table is keyed by. The
            // first two are the ends of the range; the third is the day after
            // it, written a minute past midnight in Berlin so a read that fell
            // back to UTC would place it inside the range and pass.
            store.Add(APlay(Ada, Noon(2026, 1, 1)));
            store.Add(APlay(Ada, Noon(2026, 12, 31)));
            store.Add(APlay(Ada, JustAfterMidnight(2027, 1, 1)));
            store.Add(APlay(Ada, Noon(2025, 12, 31)));
            store.Add(APlay(Bob, Noon(2026, 6, 1)));
        }

        using var reading = new SqlitePlayStore(_root, Berlin);

        var year = reading.RollupsFor(Ada, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), 1_000);

        Assert.Equal(
            new[] { new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31) },
            year.Select(rollup => rollup.Day));
        Assert.All(year, rollup => Assert.Equal(Ada, rollup.UserId));
    }

    /// <summary>
    /// The figures on a row the range read hands back are the figures the walk
    /// hands back for the same row.
    /// </summary>
    /// <remarks>
    /// The two statements name the same eleven columns in the same order and
    /// read them through one function, and this is what says so. A range read
    /// that had its own copy of the ordinals would go on reading the row it used
    /// to the day a column moved, and every figure would be wrong by one column
    /// with nothing saying which.
    /// </remarks>
    [Fact]
    public void ARowReadOverARangeIsTheRowTheWalkHandsBack()
    {
        Directory.CreateDirectory(_root);

        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, Noon(2026, 5, 4), TimeSpan.FromMinutes(20), reachedTheEnd: true));
            store.Add(APlay(Ada, Noon(2026, 5, 4), TimeSpan.FromMinutes(31), reachedTheEnd: false, method: PlayMethod.Transcode));
        }

        using var reading = new SqlitePlayStore(_root, Berlin);

        var walked = Assert.Single(reading.AllRollups(), rollup => rollup.UserId == Ada);
        var ranged = Assert.Single(reading.RollupsFor(Ada, new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 1), 1_000));

        Assert.Equal(walked, ranged);
        Assert.Equal(2, ranged.Plays);
        Assert.Equal(TimeSpan.FromMinutes(51), ranged.Watched);
        Assert.Equal(1, ranged.Completed);
        Assert.Equal(1, ranged.Transcode);
        Assert.Equal(1, ranged.DirectPlay);
    }

    /// <summary>
    /// The read stops at the bound it was given and says nothing about a range
    /// that held more, which is what makes a caller ask for one row more than it
    /// will accept.
    /// </summary>
    /// <remarks>
    /// The store deciding for itself would be the truncation issue #56 refused
    /// for plays: an answer short by rows nobody named reads exactly like a
    /// complete one. What this asserts is that the bound bites at all, so a
    /// statement that lost its <c>LIMIT</c> is caught here rather than by a
    /// server with a decade of days on it.
    /// </remarks>
    [Fact]
    public void TheReadStopsAtTheBoundItWasGiven()
    {
        Directory.CreateDirectory(_root);

        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            for (var day = 1; day <= 5; day++)
            {
                store.Add(APlay(Ada, Noon(2026, 4, day)));
            }
        }

        using var reading = new SqlitePlayStore(_root, Berlin);

        var whole = reading.RollupsFor(Ada, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), 1_000);
        var bounded = reading.RollupsFor(Ada, new DateOnly(2026, 4, 1), new DateOnly(2026, 5, 1), 3);

        Assert.Equal(5, whole.Count);
        Assert.Equal(3, bounded.Count);

        // The rows that did come back are the first three of the range and not
        // three of it. A bound that handed back an arbitrary three would make a
        // caller asking for one more row than it accepts prove nothing.
        Assert.Equal(whole.Take(3).Select(rollup => rollup.Day), bounded.Select(rollup => rollup.Day));
    }

    /// <summary>
    /// A range this store holds nothing in comes back empty rather than as
    /// anything else.
    /// </summary>
    [Fact]
    public void ARangeWithNothingInItIsEmpty()
    {
        Directory.CreateDirectory(_root);

        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, Noon(2026, 4, 1)));
        }

        using var reading = new SqlitePlayStore(_root, Berlin);

        Assert.Empty(reading.RollupsFor(Ada, new DateOnly(2020, 1, 1), new DateOnly(2021, 1, 1), 1_000));
        Assert.Empty(reading.RollupsFor(Bob, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), 1_000));
    }

    /// <summary>
    /// A range that ends before it starts, and a bound of none, are refused
    /// rather than answered with no rows.
    /// </summary>
    /// <remarks>
    /// Both mistakes produce an empty list from the statement, and an empty list
    /// is what an account that recorded nothing looks like. A wrap-up folded
    /// from either would tell somebody they watched nothing that year.
    /// </remarks>
    [Fact]
    public void ARangeThatEndsBeforeItStartsAndABoundOfNoneAreRefused()
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Berlin);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.RollupsFor(Ada, new DateOnly(2027, 1, 1), new DateOnly(2026, 1, 1), 1_000));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.RollupsFor(Ada, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.RollupsFor(Ada, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), -1));

        // A range of no length at all is a caller asking about nothing rather
        // than a caller making a mistake, and it answers rather than throws.
        Assert.Empty(store.RollupsFor(Ada, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 1_000));
    }

    private static DateTime Noon(int year, int month, int day)
        => TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Unspecified), Berlin);

    private static DateTime JustAfterMidnight(int year, int month, int day)
        => TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, 0, 1, 0, DateTimeKind.Unspecified), Berlin);

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan? watched = null,
        bool reachedTheEnd = true,
        PlayMethod method = PlayMethod.DirectPlay)
    {
        var length = watched ?? TimeSpan.FromMinutes(20);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = new Guid("33333333333333333333333333333333"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(length),
            WatchedDuration = length,
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
                VideoWasDirect = method != PlayMethod.Transcode,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    /// <summary>
    /// The range statement, read out of the store's own source.
    /// </summary>
    /// <returns>The statement.</returns>
    private static string TheRangeStatement() => StatementCalled("SelectARollupRange");

    /// <summary>
    /// The walk, read the same way.
    /// </summary>
    /// <returns>The statement.</returns>
    private static string TheWalkStatement() => StatementCalled("SelectEveryRollup");

    /// <summary>
    /// Reads one of the store's verbatim string constants.
    /// </summary>
    /// <remarks>
    /// A copy in this file would be measured instead of the statement, and would
    /// go on passing after the store's own was rewritten. The comment line each
    /// statement opens with is dropped, because it is the marker the bound rule
    /// reads and not part of what SQLite is asked to plan.
    /// </remarks>
    /// <param name="name">The constant's name.</param>
    /// <returns>The statement.</returns>
    private static string StatementCalled(string name)
    {
        var source = File.ReadAllText("Jellyfin.Plugin.Stats/Data/SqlitePlayStore.cs".Repositioned());
        var at = source.IndexOf("private const string " + name + " =", StringComparison.Ordinal);

        Assert.True(at >= 0, name + " was not found in the store's source, so nothing here is planning it.");

        var opens = source.IndexOf('"', source.IndexOf("@\"", at, StringComparison.Ordinal)) + 1;
        var closes = source.IndexOf("\";", opens, StringComparison.Ordinal);

        Assert.True(closes > opens, name + " does not end where a verbatim string ends.");

        var statement = source[opens..closes].Replace("\"\"", "\"", StringComparison.Ordinal);
        var body = new StringBuilder();

        foreach (var line in statement.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("--", StringComparison.Ordinal))
            {
                body.Append(line).Append('\n');
            }
        }

        return body.ToString();
    }

    /// <summary>
    /// The plan SQLite makes for one statement, as one string.
    /// </summary>
    /// <param name="connection">The store's file.</param>
    /// <param name="statement">What to plan.</param>
    /// <param name="bind">Binds whatever the statement names.</param>
    /// <returns>Every line of the plan.</returns>
    private static string PlanOf(SqliteConnection connection, string statement, Action<SqliteCommand> bind)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ExplainPrefix + statement;
        bind(command);

        var plan = new StringBuilder();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            plan.Append(reader.GetString(reader.GetOrdinal("detail"))).Append('\n');
        }

        return plan.ToString();
    }

    /// <summary>
    /// The store's own file, opened directly so a plan can be asked for.
    /// </summary>
    /// <returns>The connection.</returns>
    private SqliteConnection OpenTheFile()
    {
        Directory.CreateDirectory(_root);

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_root, SqlitePlayStore.FileName),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());

        connection.Open();

        return connection;
    }

    /// <summary>
    /// Three years of days for a dozen accounts, written straight into the
    /// table.
    /// </summary>
    /// <param name="connection">The store's file.</param>
    private static void SeedRollups(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            @"INSERT INTO daily_rollups (
                  Day, UserId, ItemType, ClientName,
                  Plays, WatchedDurationTicks, Completed,
                  UnknownMethod, DirectPlay, DirectStream, Transcode
              ) VALUES ($day, $userId, 'Episode', 'Jellyfin Web', 1, 1, 1, 0, 1, 0, 0)";

        var day = command.Parameters.Add("$day", SqliteType.Text);
        var user = command.Parameters.Add("$userId", SqliteType.Text);
        var accounts = Accounts();

        for (var offset = 0; offset < SeededDays; offset++)
        {
            var on = new DateOnly(2025, 1, 1).AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            foreach (var account in accounts)
            {
                day.Value = on;
                user.Value = account;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    /// <summary>
    /// A dozen accounts, one of which is the one the plan above asks about.
    /// </summary>
    /// <returns>The accounts, as the store writes them.</returns>
    private static IReadOnlyList<string> Accounts()
    {
        var accounts = new List<string> { Ada.ToString("N", CultureInfo.InvariantCulture) };

        for (var index = 1; index < SeededAccounts; index++)
        {
            accounts.Add(new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0).ToString("N", CultureInfo.InvariantCulture));
        }

        return accounts;
    }
}
