using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// The store, as a SQLite file in a folder the caller names.
/// </summary>
/// <remarks>
/// This is the one class in the plugin that knows what the store is made of.
/// Everything else talks to <see cref="IPlayStore"/>, so the answer to the first
/// question in issue #10 can be revisited without a caller changing.
/// <para>
/// The folder is passed in rather than read off the plugin instance. A running
/// server hands it its own data folder and a test hands it a temporary
/// directory, and neither route is the special one.
/// </para>
/// <para>
/// One connection is held open for the life of the store rather than one being
/// opened per statement. SQLite admits a single writer, and a connection opened
/// and closed around every play turns a write into a file open, a lock and a
/// close on the thread that got there first.
/// </para>
/// </remarks>
public sealed class SqlitePlayStore : IPlayStore
{
    /// <summary>
    /// The file the store lives in, inside the folder it is given. Named here
    /// rather than by a caller: two callers naming it is two stores.
    /// </summary>
    public const string FileName = "plays.db";

    private const string InsertPlay =
        @"INSERT INTO plays (
              SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
              StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
              ClientName, DeviceId, DeviceName, PlayMethod,
              TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
              TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons
          ) VALUES (
              $schemaVersion, $userId, $itemId, $itemType, $parentId, $itemName, $itemRuntimeTicks,
              $startedUtcTicks, $endedUtcTicks, $watchedDurationTicks, $reachedTheEnd,
              $clientName, $deviceId, $deviceName, $playMethod,
              $transcodeVideoCodec, $transcodeAudioCodec, $transcodeVideoWasDirect, $transcodeAudioWasDirect,
              $transcodePeakBitrate, $transcodeTypicalBitrate, $transcodeHardwareAcceleration, $transcodeReasons
          )";

    // Spelled out rather than assembled from a shared column list, because
    // assembling it is the concatenation the invariant rule refuses, and the
    // reason it refuses it is that a statement built from strings is a statement
    // whose shape depends on its input. The ordinals below follow this order.
    private const string SelectMostRecent =
        @"-- bound: $limit
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethod,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons
          FROM plays
          ORDER BY StartedUtcTicks DESC, Id DESC
          LIMIT $limit";

    // The export's two reads. Spelled out for the same reason as the select
    // above: a column list shared between statements has to be pasted into them
    // to be used, and pasting is the concatenation the invariant rule refuses.
    //
    // Both order by Id, which is the order the rows were written. An export is
    // compared against another export, and StartedUtcTicks is not unique: two
    // plays that started in the same tick would come back in whichever order
    // the query planner happened to produce, and a round trip would then differ
    // from its original for a reason that has nothing to do with the archive.
    private const string SelectEveryPlay =
        @"-- unbounded: walked
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethod,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons
          FROM plays
          ORDER BY Id";

    private const string SelectEveryPlayOfAUser =
        @"-- unbounded: walked
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethod,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons
          FROM plays
          WHERE UserId = $userId
          ORDER BY Id";

    // One row per account rather than one per play, which is what lets this
    // statement carry no limit where every other read here does. The store
    // reduces the column rather than handing back a million rows for a caller
    // to put into a set, and the order is the column's own so two runs over an
    // unchanged file answer in the same order.
    private const string SelectTheUsersWithPlays =
        @"-- unbounded: one row per account
          SELECT DISTINCT UserId
          FROM plays
          ORDER BY UserId";

    // One moment however many rows the table holds, which is what lets this
    // statement carry no limit either. It reduces the started column rather
    // than ordering the table and taking a row off the front, so nothing here
    // reads a row at all.
    //
    // Over an empty table the aggregate still answers, with one row holding
    // null, and that null is the answer rather than a missing one. The read
    // below turns it into an absent moment instead of a moment at the first
    // tick a clock can name.
    private const string SelectTheOldestStart =
        @"-- unbounded: one number
          SELECT MIN(StartedUtcTicks)
          FROM plays";

    // One account's earliest start at or after a moment, which is what walking
    // the years an account has rows in is made of. It reduces the started column
    // under an equality on the account, so the pair index serves both halves and
    // no row is handed back.
    //
    // Asked once per year answered plus once more, rather than once per year
    // between the first and the last. The difference is the whole point: a year
    // with no rows is stepped straight over instead of being asked about and
    // offered, and an account that watched something in 2019 and again this year
    // costs three statements rather than one per year in between.
    private const string SelectTheFirstStartAtOrAfter =
        @"-- unbounded: one number
          SELECT MIN(StartedUtcTicks)
          FROM plays
          WHERE UserId = $userId AND StartedUtcTicks >= $from";

    // The retention sweep's three statements. The count is what lets a sweep
    // say how far through it is, and it is asked once rather than per bite.
    private const string CountPlaysBefore =
        @"-- unbounded: one number
          SELECT COUNT(*)
          FROM plays
          WHERE StartedUtcTicks < $cutoff";

    // Bounded by an argument, like every other read here, and for a second
    // reason: one statement deleting a decade of rows holds the write lock for
    // its whole duration and answers no cancellation while it runs. The inner
    // select is where the bound goes, because a limit on the delete itself is a
    // SQLite build option rather than a guarantee, and a statement that depends
    // on how the native library was compiled is a statement that works here and
    // fails on somebody's server.
    private const string DeletePlaysBefore =
        @"DELETE FROM plays
          WHERE Id IN (
              SELECT Id
              FROM plays
              WHERE StartedUtcTicks < $cutoff
              ORDER BY Id
              LIMIT $limit
          )";

    // The same shape one column over, for the account that was deleted. It is
    // spelled out rather than folded into the statement above with a second
    // condition, because a statement whose WHERE clause is assembled from what
    // the caller passed is the thing the concatenation rule refuses, and a
    // condition that is sometimes there is that assembled clause written in
    // C# instead of in SQL.
    //
    // The bound sits in the inner select for the same reason it does above.
    private const string DeletePlaysOfAUser =
        @"DELETE FROM plays
          WHERE Id IN (
              SELECT Id
              FROM plays
              WHERE UserId = $userId
              ORDER BY Id
              LIMIT $limit
          )";

    // The same shape again with the window the account named, for the account
    // deleting part of its own history. A third statement rather than a
    // condition added to the one above where a caller passed bounds: a WHERE
    // clause that is sometimes two conditions and sometimes three is assembled
    // in C# and pasted into the text, which is what the concatenation rule
    // refuses, and each of the three here is a constant that never moves.
    //
    // The window is half open, so two windows laid end to end delete each row
    // once. The bound sits in the inner select for the same reason it does
    // above.
    private const string DeletePlaysOfAUserBetween =
        @"DELETE FROM plays
          WHERE Id IN (
              SELECT Id
              FROM plays
              WHERE UserId = $userId AND StartedUtcTicks >= $from AND StartedUtcTicks < $to
              ORDER BY Id
              LIMIT $limit
          )";

    // What turns freed pages back into free disk. It rewrites the file, so it
    // wants room for a second copy and it cannot run inside a transaction;
    // neither is a problem where it is called, once, at the end of a sweep.
    private const string ReclaimTheFile = "VACUUM";

    // Transcode reasons arrive as a set of short identifiers the server names,
    // and they are read back as a whole or not at all, so they travel in one
    // column. A table of its own would be a join on every read of every report
    // for a value nothing ever queries by.
    //
    // The separator is a character the server's reason identifiers do not
    // contain. One that did would read back as two reasons, quietly, so a
    // reason carrying it is refused at the write rather than trusted to the
    // sentence above.
    private const char ReasonSeparator = '|';

    private readonly SqliteConnection _connection;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlitePlayStore"/> class,
    /// creating the folder, the file and the schema where they are not there
    /// yet.
    /// </summary>
    /// <param name="dataFolderPath">The folder the store file belongs in.</param>
    public SqlitePlayStore(string dataFolderPath)
    {
        ArgumentNullException.ThrowIfNull(dataFolderPath);

        // Both of these are already-there safe, so first use and every use
        // afterwards take the same path and there is no start-up branch that
        // only ever runs once and is therefore never exercised again.
        Directory.CreateDirectory(dataFolderPath);

        // Pooling off. The store holds its one connection for its own lifetime,
        // so a pool behind it never saves an open, and what it does instead is
        // keep the file open after the store has been disposed of. On Windows
        // that is not a detail: the handle refuses the deletion of the data
        // folder, which is what an uninstall does, and the first thing it broke
        // was this suite's own clean-up.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataFolderPath, FileName),
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        try
        {
            // Opening is migrating. A store is created by running every step
            // over an empty file and an existing one by running the steps it
            // has not had yet, so there is one route rather than a create path
            // and an upgrade path that can disagree about what the schema is.
            SchemaMigrator.MigrateToLatest(_connection, SchemaMigrations.All);
        }
        catch
        {
            // A constructor that throws leaves nothing behind for anybody to
            // dispose of, and the connection above is already open, so without
            // this the file handle survives the failure with no owner. The
            // write path opens a store per finished play until one succeeds, so
            // one refusal is not one leaked handle, it is one per play for as
            // long as the plugin is installed, and on Windows every one of them
            // refuses the deletion of the data folder that an uninstall does.
            // The failure it was found by is a store from a later build, which
            // is the refusal that is meant to be survivable.
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the schema this build writes. It is stamped on every row, which is
    /// what lets a reader tell what shape a row is in without asking the file
    /// what version it is at.
    /// </summary>
    public static int SchemaVersion => SchemaMigrations.Latest;

    /// <inheritdoc />
    public void Add(PlayRecord play)
    {
        ArgumentNullException.ThrowIfNull(play);

        using var command = _connection.CreateCommand();
        command.CommandText = InsertPlay;

        command.Parameters.AddWithValue("$schemaVersion", play.SchemaVersion);
        command.Parameters.AddWithValue("$userId", Text(play.UserId));
        command.Parameters.AddWithValue("$itemId", Text(play.ItemId));
        command.Parameters.AddWithValue("$itemType", play.ItemType);
        command.Parameters.AddWithValue("$parentId", Text(play.ParentId));
        command.Parameters.AddWithValue("$itemName", play.ItemName);
        command.Parameters.AddWithValue("$itemRuntimeTicks", Ticks(play.ItemRuntime));
        command.Parameters.AddWithValue("$startedUtcTicks", UtcTicks(play.StartedUtc, nameof(play.StartedUtc)));
        command.Parameters.AddWithValue("$endedUtcTicks", UtcTicks(play.EndedUtc, nameof(play.EndedUtc)));
        command.Parameters.AddWithValue("$watchedDurationTicks", play.WatchedDuration.Ticks);
        command.Parameters.AddWithValue("$reachedTheEnd", play.ReachedTheEnd);
        command.Parameters.AddWithValue("$clientName", play.ClientName);
        command.Parameters.AddWithValue("$deviceId", play.DeviceId);
        command.Parameters.AddWithValue("$deviceName", play.DeviceName);
        command.Parameters.AddWithValue("$playMethod", (int)play.PlayMethod);
        command.Parameters.AddWithValue("$transcodeVideoCodec", Text(play.Transcode.VideoCodec));
        command.Parameters.AddWithValue("$transcodeAudioCodec", Text(play.Transcode.AudioCodec));
        command.Parameters.AddWithValue("$transcodeVideoWasDirect", play.Transcode.VideoWasDirect);
        command.Parameters.AddWithValue("$transcodeAudioWasDirect", play.Transcode.AudioWasDirect);
        command.Parameters.AddWithValue("$transcodePeakBitrate", Number(play.Transcode.PeakBitrate));
        command.Parameters.AddWithValue("$transcodeTypicalBitrate", Number(play.Transcode.TypicalBitrate));
        command.Parameters.AddWithValue("$transcodeHardwareAcceleration", Text(play.Transcode.HardwareAcceleration));
        command.Parameters.AddWithValue("$transcodeReasons", JoinReasons(play.Transcode.Reasons));

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public IReadOnlyList<PlayRecord> MostRecentPlays(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var command = _connection.CreateCommand();
        command.CommandText = SelectMostRecent;
        command.Parameters.AddWithValue("$limit", limit);

        var plays = new List<PlayRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            plays.Add(ReadPlay(reader));
        }

        return plays;
    }

    /// <inheritdoc />
    /// <remarks>
    /// An iterator rather than a method returning a list, so the rows arrive
    /// one at a time and the whole store is never in memory at once. It also
    /// puts the command and the reader inside the enumerator's own lifetime:
    /// they are opened on the first row asked for and closed when the walk is
    /// disposed of, which is what a foreach does at its end and what a caller
    /// that stops early does too.
    /// </remarks>
    public IEnumerable<PlayRecord> AllPlays()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectEveryPlay;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadPlay(reader);
        }
    }

    /// <inheritdoc />
    public IEnumerable<PlayRecord> PlaysFor(Guid userId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectEveryPlayOfAUser;
        command.Parameters.AddWithValue("$userId", Text(userId));

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadPlay(reader);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A list rather than an iterator, unlike the two walks above. The caller
    /// asks the server about every identifier this returns and then deletes
    /// against the same store, and a reader left open over the table while
    /// deletions run against it is a reader whose remaining rows are whatever
    /// the deletions left. The set is one entry per account, so holding it all
    /// at once is the cheap half of this operation.
    /// </remarks>
    public IReadOnlyList<Guid> UserIdsWithPlays()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectTheUsersWithPlays;

        var users = new List<Guid>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Read back the way every other read here reads an identifier, so
            // a value that came out of this list is a value the delete and the
            // per-user read will both match.
            users.Add(Guid.ParseExact(reader.GetString(0), "N"));
        }

        return users;
    }

    /// <inheritdoc />
    public DateTime? OldestPlayStartedUtc()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectTheOldestStart;

        // Two different nulls arrive here as one. An empty table gives the
        // aggregate no row to reduce and it answers null; the column itself is
        // NOT NULL, so a row can never contribute one. The store therefore
        // reads this null as "no rows" rather than as "a row with no start",
        // and the schema is what makes that reading safe rather than a guess.
        var oldest = command.ExecuteScalar();

        return oldest is null or DBNull ? null : Utc((long)oldest);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Walked forwards from the account's first row rather than derived from a
    /// span. Each statement asks for the earliest start at or after a moment;
    /// the year that moment falls in is a year with rows in it by construction,
    /// and the next question starts at the first instant of the year after that
    /// one. So the loop answers a year per statement and stops on the statement
    /// that finds nothing, and a gap of empty years between two that have rows
    /// costs one step rather than one step each.
    /// <para>
    /// Where a local year begins is <see cref="LocalDay"/>'s answer and not a
    /// second one written here. A store that decided its own year boundary would
    /// disagree with the fold that computes the wrap-up on exactly the plays
    /// either side of midnight on the first of January, which is the disagreement
    /// nobody notices because both answers look like years.
    /// </para>
    /// </remarks>
    public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var years = new List<int>();

        // The first instant a row can carry. Ticks rather than a date, because
        // that is the column's own type and the comparison is the one the index
        // is ordered by.
        var from = 0L;

        while (true)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SelectTheFirstStartAtOrAfter;

            // Through the same Text as the write and as PlaysFor. A Guid
            // formatted any other way is a string the column does not hold, and
            // this would then answer that a real account has watched nothing.
            command.Parameters.AddWithValue("$userId", Text(userId));
            command.Parameters.AddWithValue("$from", from);

            var next = command.ExecuteScalar();
            if (next is null or DBNull)
            {
                // No row left at or after that moment. On the first pass this is
                // an account with nothing stored, and the empty list is the
                // answer rather than a year standing in for one.
                return years;
            }

            var year = LocalDay.Of(Utc((long)next), zone).Year;
            years.Add(year);

            // The last year a calendar can name has no next one to ask about,
            // and building its first of January would be the failure rather
            // than the end of the walk.
            if (year >= DateTime.MaxValue.Year)
            {
                return years;
            }

            from = LocalDay.StartOf(new DateOnly(year + 1, 1, 1), zone).UtcTicks;
        }
    }

    /// <inheritdoc />
    public long CountPlaysStartedBefore(DateTime cutoffUtc)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = CountPlaysBefore;
        command.Parameters.AddWithValue("$cutoff", UtcTicks(cutoffUtc, nameof(cutoffUtc)));

        return (long)command.ExecuteScalar()!;
    }

    /// <inheritdoc />
    public int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var command = _connection.CreateCommand();
        command.CommandText = DeletePlaysBefore;
        command.Parameters.AddWithValue("$cutoff", UtcTicks(cutoffUtc, nameof(cutoffUtc)));
        command.Parameters.AddWithValue("$limit", limit);

        return command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public int DeletePlaysFor(Guid userId, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var command = _connection.CreateCommand();
        command.CommandText = DeletePlaysOfAUser;

        // Through the same Text as the write and as PlaysFor. A Guid formatted
        // any other way is a string the column does not hold, and the deletion
        // would then match nothing and report a clean zero.
        command.Parameters.AddWithValue("$userId", Text(userId));
        command.Parameters.AddWithValue("$limit", limit);

        return command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A window whose end is at or before its start is refused rather than
    /// answered with nought. Nought is what a window holding no rows answers,
    /// and a caller who swapped their two bounds would read that as their
    /// history having nothing in it and stop asking.
    /// </remarks>
    public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var from = UtcTicks(fromUtc, nameof(fromUtc));
        var to = UtcTicks(toUtc, nameof(toUtc));

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to, nameof(fromUtc));

        using var command = _connection.CreateCommand();
        command.CommandText = DeletePlaysOfAUserBetween;

        // Through the same Text as the write and as PlaysFor, for the reason
        // the deletion above gives.
        command.Parameters.AddWithValue("$userId", Text(userId));
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$limit", limit);

        return command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void ReclaimFreedSpace()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = ReclaimTheFile;

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Turns one row into a record. The ordinals follow the order the select
    /// above names its columns in.
    /// </summary>
    /// <param name="reader">A reader standing on a row.</param>
    /// <returns>The row as a record.</returns>
    private static PlayRecord ReadPlay(SqliteDataReader reader)
    {
        return new PlayRecord
        {
            SchemaVersion = reader.GetInt32(0),
            UserId = Guid.ParseExact(reader.GetString(1), "N"),
            ItemId = Guid.ParseExact(reader.GetString(2), "N"),
            ItemType = reader.GetString(3),
            ParentId = GuidOrNull(reader, 4),
            ItemName = reader.GetString(5),
            ItemRuntime = TimeSpanOrNull(reader, 6),
            StartedUtc = Utc(reader.GetInt64(7)),
            EndedUtc = Utc(reader.GetInt64(8)),
            WatchedDuration = TimeSpan.FromTicks(reader.GetInt64(9)),
            ReachedTheEnd = reader.GetBoolean(10),
            ClientName = reader.GetString(11),
            DeviceId = reader.GetString(12),
            DeviceName = reader.GetString(13),
            PlayMethod = (PlayMethod)reader.GetInt32(14),
            Transcode = new TranscodeSummary
            {
                VideoCodec = TextOrNull(reader, 15),
                AudioCodec = TextOrNull(reader, 16),
                VideoWasDirect = reader.GetBoolean(17),
                AudioWasDirect = reader.GetBoolean(18),
                PeakBitrate = IntOrNull(reader, 19),
                TypicalBitrate = IntOrNull(reader, 20),
                HardwareAcceleration = TextOrNull(reader, 21),
                Reasons = Reasons(reader.GetString(22))
            }
        };
    }

    /// <summary>
    /// Reads a timestamp back as the moment it was, in UTC.
    /// </summary>
    /// <remarks>
    /// Ticks alone carry no zone, so a value read back without this is
    /// unspecified, and an unspecified time silently becomes local the first
    /// time anything converts it.
    /// </remarks>
    /// <param name="ticks">The stored ticks.</param>
    /// <returns>The moment, in UTC.</returns>
    private static DateTime Utc(long ticks) => new(ticks, DateTimeKind.Utc);

    /// <summary>
    /// Takes the ticks of a timestamp that has to be in UTC, and refuses one
    /// that is not.
    /// </summary>
    /// <remarks>
    /// A local or unspecified time written here would be stored as if it were
    /// UTC and read back as a different moment, off by the writer's offset, with
    /// nothing on the row saying so. That is not recoverable afterwards, which
    /// is why it is refused at the write rather than corrected at the read.
    /// </remarks>
    /// <param name="value">The timestamp.</param>
    /// <param name="name">Which timestamp it is, for the message.</param>
    /// <returns>The ticks to store.</returns>
    private static long UtcTicks(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} is {1} and the store keeps every timestamp in UTC. Storing it would move the moment by the writer's offset and leave nothing on the row saying so.",
                    name,
                    value.Kind),
                nameof(value));
        }

        return value.Ticks;
    }

    /// <summary>
    /// Puts the reasons into one column, and refuses one that would not come
    /// back out whole.
    /// </summary>
    /// <remarks>
    /// A reason carrying the separator would read back as two reasons with
    /// nothing saying so, and a transcode report counts reasons. The refusal is
    /// at the write because that is the last moment the original is still there
    /// to refuse.
    /// </remarks>
    /// <param name="reasons">The reasons observed over the play.</param>
    /// <returns>The column to store.</returns>
    private static string JoinReasons(IReadOnlyList<string> reasons)
    {
        var carriesTheSeparator = reasons.FirstOrDefault(
            reason => reason.Contains(ReasonSeparator, StringComparison.Ordinal));

        if (carriesTheSeparator is not null)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A transcode reason carries the character the store separates reasons with, so it would read back as two: {0}",
                    carriesTheSeparator),
                nameof(reasons));
        }

        return string.Join(ReasonSeparator, reasons);
    }

    /// <summary>
    /// Splits the stored reasons back into a list.
    /// </summary>
    /// <param name="stored">The stored column.</param>
    /// <returns>The reasons, and an empty list where there were none.</returns>
    private static string[] Reasons(string stored)
    {
        if (stored.Length == 0)
        {
            // Splitting an empty string yields one empty entry rather than
            // nothing, so a play with no reasons would read back as a play with
            // one reason whose name is the empty string.
            return Array.Empty<string>();
        }

        return stored.Split(ReasonSeparator);
    }

    private static Guid? GuidOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Guid.ParseExact(reader.GetString(ordinal), "N");

    private static TimeSpan? TimeSpanOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : TimeSpan.FromTicks(reader.GetInt64(ordinal));

    private static int? IntOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);

    private static string? TextOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string Text(Guid value) => value.ToString("N", CultureInfo.InvariantCulture);

    private static object Text(Guid? value)
        => value is null ? DBNull.Value : value.Value.ToString("N", CultureInfo.InvariantCulture);

    private static object Text(string? value) => value is null ? DBNull.Value : value;

    private static object Ticks(TimeSpan? value) => value is null ? DBNull.Value : value.Value.Ticks;

    private static object Number(int? value) => value is null ? DBNull.Value : value.Value;
}
