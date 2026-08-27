using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Configuration;
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
              ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
              TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
              TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
          ) VALUES (
              $schemaVersion, $userId, $itemId, $itemType, $parentId, $itemName, $itemRuntimeTicks,
              $startedUtcTicks, $endedUtcTicks, $watchedDurationTicks, $reachedTheEnd,
              $clientName, $deviceId, $deviceName, $playMethodAtStart, $playMethodChangedUtcTicks,
              $transcodeVideoCodec, $transcodeAudioCodec, $transcodeVideoWasDirect, $transcodeAudioWasDirect,
              $transcodePeakBitrate, $transcodeTypicalBitrate, $transcodeHardwareAcceleration, $transcodeReasons, $closedBy, $channelName
          )";

    // The open table's write. INSERT OR REPLACE rather than an upsert clause
    // naming every column twice: the key is the primary key and there is no
    // column here the write does not carry, so replacing the row and updating
    // it in place are the same answer and one of the two is half the text.
    //
    // This is what keeps one running play to one row. A session reporting every
    // ten seconds for three hours writes the same key a thousand times and
    // leaves one row behind it.
    private const string WriteTheOpenPlay =
        @"INSERT OR REPLACE INTO open_plays (
              PlayKey,
              SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
              StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
              ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
              TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
              TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
          ) VALUES (
              $playKey,
              $schemaVersion, $userId, $itemId, $itemType, $parentId, $itemName, $itemRuntimeTicks,
              $startedUtcTicks, $endedUtcTicks, $watchedDurationTicks, $reachedTheEnd,
              $clientName, $deviceId, $deviceName, $playMethodAtStart, $playMethodChangedUtcTicks,
              $transcodeVideoCodec, $transcodeAudioCodec, $transcodeVideoWasDirect, $transcodeAudioWasDirect,
              $transcodePeakBitrate, $transcodeTypicalBitrate, $transcodeHardwareAcceleration, $transcodeReasons, $closedBy, $channelName
          )";

    // The key is read last so the ordinals in front of it are the finished
    // row's own, and one function reads a row out of either table.
    private const string SelectEveryOpenPlay =
        @"-- unbounded: walked
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName,
                 PlayKey
          FROM open_plays
          ORDER BY PlayKey";

    private const string ForgetTheOpenPlay =
        "DELETE FROM open_plays WHERE PlayKey = $playKey";

    // The three removals below reach this table too, so a request to be
    // forgotten does not leave the running play behind. Each is unbounded
    // because the table holds one row per session that is playing right now,
    // which is a set the bite exists to protect the finished table from and
    // this one cannot grow into.
    private const string ForgetTheOpenPlaysOfAUser =
        "DELETE FROM open_plays WHERE UserId = $userId";

    private const string ForgetTheOpenPlaysOfAUserBetween =
        "DELETE FROM open_plays WHERE UserId = $userId AND StartedUtcTicks >= $from AND StartedUtcTicks < $to";

    private const string ForgetTheOpenPlaysBefore =
        "DELETE FROM open_plays WHERE StartedUtcTicks < $cutoff";

    // Spelled out rather than assembled from a shared column list, because
    // assembling it is the concatenation the invariant rule refuses, and the
    // reason it refuses it is that a statement built from strings is a statement
    // whose shape depends on its input. The ordinals below follow this order.
    private const string SelectMostRecent =
        @"-- bound: $limit
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
          FROM plays
          ORDER BY StartedUtcTicks DESC, Id DESC
          LIMIT $limit";

    // The read every aggregate report is answered from. Half open at both ends,
    // like the deletions above and for the same reason: two windows laid end to
    // end read each play once. The index on StartedUtcTicks is what makes the
    // range a seek rather than a scan of the table, and the limit is what stops
    // a report over a decade being a way to make the server do arbitrary work.
    //
    // Ordered by the moment a play started rather than by the order rows were
    // written, because what a truncated answer should hold is the oldest plays
    // in the window and not whichever ones happened to be written first. Id
    // breaks the tie, so two plays starting in the same tick come back in a
    // fixed order rather than in whichever one the planner produced.
    private const string SelectPlaysInAWindow =
        @"-- bound: $limit
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
          FROM plays
          WHERE StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY StartedUtcTicks, Id
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
                 ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
          FROM plays
          ORDER BY Id";

    private const string SelectEveryPlayOfAUser =
        @"-- unbounded: walked
          SELECT SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                 StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                 ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                 TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                 TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons, ClosedBy, ChannelName
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

    // The two statements of the deletions table. Issue #251.
    //
    // Written after the rows have gone and only where some went, so an entry
    // here stands for rows that are no longer in the file rather than for an
    // intention. A caller bites until a bite comes back empty, so recording the
    // empty one would put an entry saying nothing happened at the end of every
    // deletion this plugin performs.
    private const string RecordTheDeletion =
        "INSERT INTO deletions (Class, Rows) VALUES ($class, $rows)";

    // Newest first, which is descending over the key, because a reader asking
    // this is asking about the deletions since whatever they last saw and a
    // bound taken off an oldest-first answer is the wrong end of the table.
    private const string SelectTheDeletions =
        @"-- bound: LIMIT $limit
          SELECT Class, Rows
          FROM deletions
          ORDER BY Id DESC
          LIMIT $limit";

    // The consent table's three statements. The read is bounded by the key
    // being the primary key, which is a limit of one however many accounts the
    // server has.
    private const string SelectTheConsentOfAUser =
        @"-- bound: LIMIT 1, over a table keyed by the account
          SELECT Agreed, AgreedUtcTicks, WithdrawnUtcTicks, WordingVersion
          FROM consents
          WHERE UserId = $userId
          LIMIT 1";

    // INSERT OR REPLACE for the reason the running play's write uses it: the
    // account is the key, there is no column here the write does not carry, and
    // the question has one answer at a time.
    private const string WriteTheConsent =
        @"INSERT OR REPLACE INTO consents (
              UserId, Agreed, AgreedUtcTicks, WithdrawnUtcTicks, WordingVersion
          ) VALUES (
              $userId, $agreed, $agreedUtcTicks, $withdrawnUtcTicks, $wordingVersion
          )";

    private const string ForgetTheConsentOfAUser =
        "DELETE FROM consents WHERE UserId = $userId";

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

    // The shape a day is written in. An ISO date sorts as text in the order it
    // sorts as a date, so a range over days is a range over the column.
    private const string DayFormat = "yyyy-MM-dd";

    // How many rows a rebuild reads at once. The deletions' bite, and named
    // separately for the reason each of those is named where it is used: a size
    // shared between two operations makes a change to either one a change to
    // both. Small enough that what is held is a page rather than a store, and
    // large enough that a year of rows is not ten thousand statements.
    private const int RebuildPage = 500;

    // The day-by-day fold, written as the row that produced it is written.
    //
    // One statement rather than a read and then a write. A rollup row either
    // exists for this day, account, item type and client or it does not, and
    // asking first would be two round trips and a window between them in which
    // another writer inserts the row this one then inserts again.
    //
    // The delivery counts arrive as noughts and ones rather than as a branch
    // per method, so one play adds one to exactly one of the four columns and
    // the four always add up to the play count.
    private const string FoldThePlayIntoItsDay =
        @"INSERT INTO daily_rollups (
              Day, UserId, ItemType, ClientName,
              Plays, WatchedDurationTicks, Completed,
              UnknownMethod, DirectPlay, DirectStream, Transcode
          )
          VALUES (
              $day, $userId, $itemType, $clientName,
              1, $watched, $completed,
              $unknownMethod, $directPlay, $directStream, $transcode
          )
          ON CONFLICT (Day, UserId, ItemType, ClientName) DO UPDATE SET
              Plays = Plays + 1,
              WatchedDurationTicks = WatchedDurationTicks + excluded.WatchedDurationTicks,
              Completed = Completed + excluded.Completed,
              UnknownMethod = UnknownMethod + excluded.UnknownMethod,
              DirectPlay = DirectPlay + excluded.DirectPlay,
              DirectStream = DirectStream + excluded.DirectStream,
              Transcode = Transcode + excluded.Transcode";

    private const string SelectEveryRollup =
        @"-- unbounded: walked
          SELECT Day, UserId, ItemType, ClientName,
                 Plays, WatchedDurationTicks, Completed,
                 UnknownMethod, DirectPlay, DirectStream, Transcode
          FROM daily_rollups
          ORDER BY Day, UserId, ItemType, ClientName";

    // The columns a rollup is folded from, and nothing else. A rebuild and a
    // corrective deletion both walk rows to move the same eleven figures, and
    // reading the whole play row to do it would carry the item name, the device
    // and the transcode summary through a loop that never looks at them.
    //
    // Keyed off the row identifier rather than paged by an offset. An offset
    // re-reads the same rows when something is deleted underneath the walk,
    // which is the shape a rebuild running beside a sweep arrives in.
    private const string SelectTheRollupColumnsAfter =
        @"-- bound: LIMIT $limit
          SELECT Id, StartedUtcTicks, UserId, ItemType, ClientName,
                 WatchedDurationTicks, ReachedTheEnd, PlayMethodAtStart
          FROM plays
          WHERE Id > $after
          ORDER BY Id
          LIMIT $limit";

    // The same columns over each of the three deletions' own doomed sets, so a
    // corrective deletion can take those rows out of the days they were folded
    // into before they stop existing. Each mirrors the inner select of the
    // statement beside it exactly - the same condition, the same order and the
    // same bound - because a set that differed by one row would move a figure
    // by a play that is still in the file, or leave one standing for a play
    // that is not.
    //
    // Three statements rather than one with a condition assembled in C#, for
    // the reason the three deletions themselves are three.
    private const string SelectTheRollupColumnsBefore =
        @"-- bound: LIMIT $limit
          SELECT Id, StartedUtcTicks, UserId, ItemType, ClientName,
                 WatchedDurationTicks, ReachedTheEnd, PlayMethodAtStart
          FROM plays
          WHERE StartedUtcTicks < $cutoff
          ORDER BY Id
          LIMIT $limit";

    private const string SelectTheRollupColumnsOfAUser =
        @"-- bound: LIMIT $limit
          SELECT Id, StartedUtcTicks, UserId, ItemType, ClientName,
                 WatchedDurationTicks, ReachedTheEnd, PlayMethodAtStart
          FROM plays
          WHERE UserId = $userId
          ORDER BY Id
          LIMIT $limit";

    private const string SelectTheRollupColumnsOfAUserBetween =
        @"-- bound: LIMIT $limit
          SELECT Id, StartedUtcTicks, UserId, ItemType, ClientName,
                 WatchedDurationTicks, ReachedTheEnd, PlayMethodAtStart
          FROM plays
          WHERE UserId = $userId AND StartedUtcTicks >= $from AND StartedUtcTicks < $to
          ORDER BY Id
          LIMIT $limit";

    // Taking one play back out of the day it was folded into. The mirror of the
    // fold above, subtracting exactly what that statement added, so a day the
    // deletion emptied ends at nought on every column rather than at a
    // remainder.
    //
    // It updates and never inserts. A row that is not there is a play written
    // by a build from before this table existed, which was never folded into
    // anything, and inserting a negative row for it would put a figure in the
    // table that no play produced.
    private const string TakeThePlayOutOfItsDay =
        @"UPDATE daily_rollups SET
              Plays = Plays - 1,
              WatchedDurationTicks = WatchedDurationTicks - $watched,
              Completed = Completed - $completed,
              UnknownMethod = UnknownMethod - $unknownMethod,
              DirectPlay = DirectPlay - $directPlay,
              DirectStream = DirectStream - $directStream,
              Transcode = Transcode - $transcode
          WHERE Day = $day AND UserId = $userId AND ItemType = $itemType AND ClientName = $clientName";

    // A day nothing is left in stops being a day. A row reading nought plays is
    // not the same statement as no row: the first says the account watched
    // nothing that day, which a report would draw, and the second says there is
    // nothing to say.
    private const string ForgetTheDaysThatAreEmpty =
        "DELETE FROM daily_rollups WHERE Plays <= 0";

    // What a rebuild starts from. Not a drop: the table stays, its shape stays,
    // and what goes is every row in it.
    private const string ForgetEveryRollup = "DELETE FROM daily_rollups";

    private const string SelectTheRollupZone =
        @"-- bound: LIMIT 1, over a table that holds one row
          SELECT ZoneId
          FROM rollup_zone
          LIMIT 1";

    private const string StateTheRollupZone =
        "INSERT INTO rollup_zone (ZoneId) VALUES ($zoneId)";

    private readonly SqliteConnection _connection;

    private readonly TimeZoneInfo _rollupZone;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlitePlayStore"/> class,
    /// creating the folder, the file and the schema where they are not there
    /// yet.
    /// </summary>
    /// <param name="dataFolderPath">The folder the store file belongs in.</param>
    /// <param name="rollupZone">
    /// The zone days are counted in where this file has not stated one yet.
    /// Where it has, the stated zone stands and this argument is not used, for
    /// the reason written at <see cref="RollupZone"/>. Null is the zone the
    /// configuration model defaults to, so a caller with no opinion gets the
    /// same answer as one that read the default and passed it.
    /// </param>
    public SqlitePlayStore(string dataFolderPath, TimeZoneInfo? rollupZone = null)
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

            // Read before written, and written only where the file states
            // nothing. A store that has already keyed rollups in one zone keeps
            // that zone whatever this process is configured with, because the
            // rows already there mean days in it and rekeying them is a rebuild
            // rather than a setting taking effect.
            _rollupZone = ZoneTheFileStates() ?? StateTheZone(rollupZone ?? DefaultRollupZone());
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
    public TimeZoneInfo? RollupZone => _rollupZone;

    /// <inheritdoc />
    /// <remarks>
    /// A transaction over two statements, because the row and the day it moves
    /// are one fact. A rollup that gained a play the store then failed to write
    /// is a figure standing over a row nobody can find, which is the one thing
    /// a rollup may never be.
    /// </remarks>
    public void Add(PlayRecord play)
    {
        ArgumentNullException.ThrowIfNull(play);

        using var transaction = _connection.BeginTransaction();

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = InsertPlay;
            BindThePlay(command, play);
            command.ExecuteNonQuery();
        }

        FoldIntoTheDay(transaction, play);

        transaction.Commit();
    }

    /// <inheritdoc />
    /// <remarks>
    /// One transaction over the whole sequence, which is what the interface's
    /// own remark says this is for. Written a row at a time each row is its own
    /// transaction and its own flush, and what that costs was measured rather
    /// than assumed, under issue #56, where the two writes are timed against
    /// each other. What the difference buys is a store large enough to measure
    /// a report over, which at the rate of one flush per row nobody can build.
    /// <para>
    /// The command is built once and its parameters are replaced per row, for
    /// the same reason the transaction is one: preparing the same statement a
    /// thousand times is a thousand parses of one string.
    /// </para>
    /// <para>
    /// A failure anywhere leaves none of them, and the interface says so. That
    /// is the difference between this and the loop, and it is why the archive
    /// import - which is written to keep the rows before a bad line - does not
    /// call it.
    /// </para>
    /// </remarks>
    public void AddMany(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        using var transaction = _connection.BeginTransaction();

        using (var command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = InsertPlay;

            foreach (var play in plays)
            {
                ArgumentNullException.ThrowIfNull(play);

                command.Parameters.Clear();
                BindThePlay(command, play);
                command.ExecuteNonQuery();

                FoldIntoTheDay(transaction, play);
            }
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public void NoteOpenPlay(OpenPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentException.ThrowIfNullOrEmpty(play.PlayKey);

        using var command = _connection.CreateCommand();
        command.CommandText = WriteTheOpenPlay;
        command.Parameters.AddWithValue("$playKey", play.PlayKey);
        BindThePlay(command, play.SoFar);

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentException.ThrowIfNullOrEmpty(playKey);

        // One transaction over both statements. The finished row and the open
        // row apart are how one play becomes two, and the window between two
        // separate writes is exactly the moment a restart is most likely to
        // land in: the server is stopping and every session is finishing at
        // once.
        using var transaction = _connection.BeginTransaction();

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = InsertPlay;
            BindThePlay(insert, play);
            insert.ExecuteNonQuery();
        }

        using (var forget = _connection.CreateCommand())
        {
            forget.Transaction = transaction;
            forget.CommandText = ForgetTheOpenPlay;
            forget.Parameters.AddWithValue("$playKey", playKey);
            forget.ExecuteNonQuery();
        }

        FoldIntoTheDay(transaction, play);

        transaction.Commit();
    }

    /// <inheritdoc />
    public void ForgetOpenPlay(string playKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(playKey);

        using var command = _connection.CreateCommand();
        command.CommandText = ForgetTheOpenPlay;
        command.Parameters.AddWithValue("$playKey", playKey);

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    /// <remarks>
    /// An iterator for the reason the export's walks are iterators, and the
    /// key is the last column so the ordinals in front of it are the finished
    /// row's own and one function reads a row out of either table.
    /// </remarks>
    public IEnumerable<OpenPlay> OpenPlays()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectEveryOpenPlay;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new OpenPlay
            {
                SoFar = ReadPlay(reader),
                PlayKey = reader.GetString(26)
            };
        }
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
    public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var command = _connection.CreateCommand();
        command.CommandText = SelectPlaysInAWindow;
        command.Parameters.AddWithValue("$from", UtcTicks(fromUtc, nameof(fromUtc)));
        command.Parameters.AddWithValue("$to", UtcTicks(toUtc, nameof(toUtc)));
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
    /// <remarks>
    /// An iterator for the same reason the walks above are, and ordered by the
    /// key so two stores holding the same rollups walk them in the same order.
    /// </remarks>
    public IEnumerable<DailyRollup> AllRollups()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectEveryRollup;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return new DailyRollup
            {
                Day = DateOnly.ParseExact(reader.GetString(0), DayFormat, CultureInfo.InvariantCulture),
                UserId = Guid.ParseExact(reader.GetString(1), "N"),
                ItemType = reader.GetString(2),
                ClientName = reader.GetString(3),
                Plays = reader.GetInt64(4),
                Watched = TimeSpan.FromTicks(reader.GetInt64(5)),
                Completed = reader.GetInt64(6),
                UnknownMethod = reader.GetInt64(7),
                DirectPlay = reader.GetInt64(8),
                DirectStream = reader.GetInt64(9),
                Transcode = reader.GetInt64(10)
            };
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
    public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Declared(deletionClass);

        var cutoff = UtcTicks(cutoffUtc, nameof(cutoffUtc));

        using var transaction = _connection.BeginTransaction();

        // The open rows first, and unbounded, because a play older than the
        // retention window that is still marked as running is a leftover rather
        // than a session anybody is watching, and there is one row per session
        // rather than one per play. Doing it before the bite means a sweep that
        // finds no finished rows left has still taken them.
        using (var stale = _connection.CreateCommand())
        {
            stale.Transaction = transaction;
            stale.CommandText = ForgetTheOpenPlaysBefore;
            stale.Parameters.AddWithValue("$cutoff", cutoff);
            stale.ExecuteNonQuery();
        }

        MoveTheDaysThoseRowsWereIn(
            transaction,
            deletionClass,
            SelectTheRollupColumnsBefore,
            doomed => doomed.Parameters.AddWithValue("$cutoff", cutoff),
            limit);

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeletePlaysBefore;
        command.Parameters.AddWithValue("$cutoff", cutoff);
        command.Parameters.AddWithValue("$limit", limit);

        var deleted = Recorded(transaction, command.ExecuteNonQuery(), deletionClass);
        transaction.Commit();

        return deleted;
    }

    /// <inheritdoc />
    public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Declared(deletionClass);

        using var transaction = _connection.BeginTransaction();

        // The account's running play goes too, and it goes first. A caller
        // bites until this answers nought, so anything left to the last call
        // would be left to a call that never comes.
        using (var running = _connection.CreateCommand())
        {
            running.Transaction = transaction;
            running.CommandText = ForgetTheOpenPlaysOfAUser;
            running.Parameters.AddWithValue("$userId", Text(userId));
            running.ExecuteNonQuery();
        }

        MoveTheDaysThoseRowsWereIn(
            transaction,
            deletionClass,
            SelectTheRollupColumnsOfAUser,
            doomed => doomed.Parameters.AddWithValue("$userId", Text(userId)),
            limit);

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeletePlaysOfAUser;

        // Through the same Text as the write and as PlaysFor. A Guid formatted
        // any other way is a string the column does not hold, and the deletion
        // would then match nothing and report a clean zero.
        command.Parameters.AddWithValue("$userId", Text(userId));
        command.Parameters.AddWithValue("$limit", limit);

        var deleted = Recorded(transaction, command.ExecuteNonQuery(), deletionClass);
        transaction.Commit();

        return deleted;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A window whose end is at or before its start is refused rather than
    /// answered with nought. Nought is what a window holding no rows answers,
    /// and a caller who swapped their two bounds would read that as their
    /// history having nothing in it and stop asking.
    /// </remarks>
    public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        Declared(deletionClass);

        var from = UtcTicks(fromUtc, nameof(fromUtc));
        var to = UtcTicks(toUtc, nameof(toUtc));

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(from, to, nameof(fromUtc));

        using var transaction = _connection.BeginTransaction();

        // The account's running play as well, where it started inside the
        // window, and first for the reason the deletion above gives.
        using (var running = _connection.CreateCommand())
        {
            running.Transaction = transaction;
            running.CommandText = ForgetTheOpenPlaysOfAUserBetween;
            running.Parameters.AddWithValue("$userId", Text(userId));
            running.Parameters.AddWithValue("$from", from);
            running.Parameters.AddWithValue("$to", to);
            running.ExecuteNonQuery();
        }

        MoveTheDaysThoseRowsWereIn(
            transaction,
            deletionClass,
            SelectTheRollupColumnsOfAUserBetween,
            doomed =>
            {
                doomed.Parameters.AddWithValue("$userId", Text(userId));
                doomed.Parameters.AddWithValue("$from", from);
                doomed.Parameters.AddWithValue("$to", to);
            },
            limit);

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeletePlaysOfAUserBetween;

        // Through the same Text as the write and as PlaysFor, for the reason
        // the deletion above gives.
        command.Parameters.AddWithValue("$userId", Text(userId));
        command.Parameters.AddWithValue("$from", from);
        command.Parameters.AddWithValue("$to", to);
        command.Parameters.AddWithValue("$limit", limit);

        var deleted = Recorded(transaction, command.ExecuteNonQuery(), deletionClass);
        transaction.Commit();

        return deleted;
    }

    /// <inheritdoc />
    public void RebuildRollups()
    {
        using var transaction = _connection.BeginTransaction();

        using (var forget = _connection.CreateCommand())
        {
            forget.Transaction = transaction;
            forget.CommandText = ForgetEveryRollup;
            forget.ExecuteNonQuery();
        }

        // Keyed off the last identifier read rather than counted, so a walk
        // over a store something else is writing to reads each row once. The
        // whole rebuild is one transaction, so a run that fails part of the way
        // leaves the rollups it started with rather than the half it had got
        // to, which is the difference between a table that is behind and a
        // table that is wrong.
        long after = 0;
        while (true)
        {
            var page = RollupColumnsOf(transaction, SelectTheRollupColumnsAfter, command => command.Parameters.AddWithValue("$after", after), RebuildPage);
            if (page.Count == 0)
            {
                break;
            }

            for (var i = 0; i < page.Count; i++)
            {
                FoldIntoTheDay(transaction, page[i]);
            }

            after = page[^1].Id;
        }

        transaction.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        using var command = _connection.CreateCommand();
        command.CommandText = SelectTheDeletions;
        command.Parameters.AddWithValue("$limit", limit);

        var recorded = new List<DeletionRecorded>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Read back through the same closed set the write went through. A
            // number the file holds that this build has no name for is a store
            // written by a later build, which is refused at the migration
            // rather than answered here, so anything that reaches this line is
            // a value this build declared.
            recorded.Add(new DeletionRecorded
            {
                Class = Declared((DeletionClass)reader.GetInt32(0)),
                Rows = reader.GetInt32(1)
            });
        }

        return recorded;
    }

    /// <inheritdoc />
    public ConsentRecord? ConsentFor(Guid userId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectTheConsentOfAUser;

        // Through the same Text as every other read of an identifier here. A
        // Guid formatted any other way is a string the column does not hold,
        // and this would then answer that somebody who has agreed never was
        // asked.
        command.Parameters.AddWithValue("$userId", Text(userId));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ConsentRecord
        {
            UserId = userId,
            Agreed = reader.GetBoolean(0),
            AgreedUtc = MomentOrNull(reader, 1),
            WithdrawnUtc = MomentOrNull(reader, 2),
            WordingVersion = reader.GetInt32(3)
        };
    }

    /// <inheritdoc />
    public void RecordConsent(ConsentRecord consent)
    {
        ArgumentNullException.ThrowIfNull(consent);

        using var command = _connection.CreateCommand();
        command.CommandText = WriteTheConsent;
        command.Parameters.AddWithValue("$userId", Text(consent.UserId));
        command.Parameters.AddWithValue("$agreed", consent.Agreed);
        command.Parameters.AddWithValue(
            "$agreedUtcTicks",
            consent.AgreedUtc is { } agreed ? UtcTicks(agreed, nameof(consent.AgreedUtc)) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$withdrawnUtcTicks",
            consent.WithdrawnUtc is { } withdrawn ? UtcTicks(withdrawn, nameof(consent.WithdrawnUtc)) : DBNull.Value);
        command.Parameters.AddWithValue("$wordingVersion", consent.WordingVersion);

        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public void ForgetConsentFor(Guid userId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = ForgetTheConsentOfAUser;
        command.Parameters.AddWithValue("$userId", Text(userId));

        command.ExecuteNonQuery();
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
    /// Puts one play's fields on a command, under the names both writes use.
    /// </summary>
    /// <remarks>
    /// One function for the finished write and the open one, because the two
    /// statements carry the same columns and two copies of this list would
    /// drift the first time a column is added to one of them. What the open
    /// write adds beyond this is its key, which is bound by the caller.
    /// </remarks>
    /// <param name="command">The command being built.</param>
    /// <param name="play">The play.</param>
    private static void BindThePlay(SqliteCommand command, PlayRecord play)
    {
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
        command.Parameters.AddWithValue("$playMethodAtStart", (int)play.PlayMethodAtStart);
        command.Parameters.AddWithValue(
            "$playMethodChangedUtcTicks",
            play.PlayMethodChangedUtc is { } changed ? UtcTicks(changed, nameof(play.PlayMethodChangedUtc)) : DBNull.Value);
        command.Parameters.AddWithValue("$transcodeVideoCodec", Text(play.Transcode.VideoCodec));
        command.Parameters.AddWithValue("$transcodeAudioCodec", Text(play.Transcode.AudioCodec));
        command.Parameters.AddWithValue("$transcodeVideoWasDirect", play.Transcode.VideoWasDirect);
        command.Parameters.AddWithValue("$transcodeAudioWasDirect", play.Transcode.AudioWasDirect);
        command.Parameters.AddWithValue("$transcodePeakBitrate", Number(play.Transcode.PeakBitrate));
        command.Parameters.AddWithValue("$transcodeTypicalBitrate", Number(play.Transcode.TypicalBitrate));
        command.Parameters.AddWithValue("$transcodeHardwareAcceleration", Text(play.Transcode.HardwareAcceleration));
        command.Parameters.AddWithValue("$transcodeReasons", JoinReasons(play.Transcode.Reasons));
        command.Parameters.AddWithValue("$closedBy", (int)play.ClosedBy);
        command.Parameters.AddWithValue("$channelName", Text(play.ChannelName));
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
            PlayMethodAtStart = (PlayMethod)reader.GetInt32(14),
            PlayMethodChangedUtc = MomentOrNull(reader, 15),
            Transcode = new TranscodeSummary
            {
                VideoCodec = TextOrNull(reader, 16),
                AudioCodec = TextOrNull(reader, 17),
                VideoWasDirect = reader.GetBoolean(18),
                AudioWasDirect = reader.GetBoolean(19),
                PeakBitrate = IntOrNull(reader, 20),
                TypicalBitrate = IntOrNull(reader, 21),
                HardwareAcceleration = TextOrNull(reader, 22),
                Reasons = Reasons(reader.GetString(23))
            },

            // Null and the not-said value are one answer here, and they arrive
            // from two different places. A row written before the column
            // existed is null; a row written since carries a number, and zero
            // is the number for a route nothing recorded. Reading them as the
            // same thing is what issue #222's second condition asks for.
            ClosedBy = reader.IsDBNull(24) ? PlayClosedBy.NotSaid : (PlayClosedBy)reader.GetInt32(24),

            // Null is the row naming no channel, which is every row written
            // before the column existed and every play that was not live
            // television. Issue #40.
            ChannelName = TextOrNull(reader, 25)
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

    // The zone the configuration model falls back to, resolved through the same
    // acceptance test the setting is written through, so a store opened with no
    // opinion and a store opened from the untouched setting key the same days.
    private static TimeZoneInfo DefaultRollupZone()
        => TimeZoneInfo.FindSystemTimeZoneById(ConfigurationLimits.DefaultRollupTimeZone);

    // Which of the four delivery columns this play adds one to. A play adds one
    // to exactly one of them, so the four add up to the play count and a reader
    // adding them can tell that nothing was dropped.
    private static (long Unknown, long DirectPlay, long DirectStream, long Transcode) DeliveryOf(PlayMethod method)
        => method switch
        {
            PlayMethod.DirectPlay => (0, 1, 0, 0),
            PlayMethod.DirectStream => (0, 0, 1, 0),
            PlayMethod.Transcode => (0, 0, 0, 1),
            _ => (1, 0, 0, 0)
        };

    private TimeZoneInfo? ZoneTheFileStates()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SelectTheRollupZone;

        var stated = command.ExecuteScalar();
        if (stated is null or DBNull)
        {
            return null;
        }

        // A zone this machine cannot resolve is the one case where the file
        // says something the process cannot act on. It is not repaired here and
        // it is not swallowed: a store that keyed its days in a zone this
        // machine does not have is a store whose rollups nobody here can read
        // as days, and answering with the default would silently move every one
        // of them.
        return TimeZoneInfo.FindSystemTimeZoneById((string)stated);
    }

    private TimeZoneInfo StateTheZone(TimeZoneInfo zone)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = StateTheRollupZone;
        command.Parameters.AddWithValue("$zoneId", zone.Id);
        command.ExecuteNonQuery();

        return zone;
    }

    // Folds one finished play into the day it belongs to. Called from inside
    // whatever transaction is writing the row, so the row and the day it moves
    // arrive together or neither does.
    private void FoldIntoTheDay(SqliteTransaction transaction, PlayRecord play)
        => FoldIntoTheDay(transaction, RollupColumns.Of(play));

    // The same fold over the columns alone, which is what a rebuild has: it
    // walks the values a rollup is made of rather than whole play rows. One
    // statement for both routes, so a rebuild cannot fold a play into a
    // different day from the one the write path put it in.
    private void FoldIntoTheDay(SqliteTransaction transaction, RollupColumns row)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = FoldThePlayIntoItsDay;
        BindTheRollupRow(command, row);
        command.ExecuteNonQuery();
    }

    // The mirror of it. Subtracts exactly what the fold added, so a rollup a
    // corrective deletion emptied ends at nought on every column rather than at
    // a remainder.
    private void TakeOutOfTheDay(SqliteTransaction transaction, RollupColumns row)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = TakeThePlayOutOfItsDay;
        BindTheRollupRow(command, row);
        command.ExecuteNonQuery();
    }

    // Takes the rows one deletion is about to remove out of the days they were
    // folded into, and only where the deletion is correcting the record.
    //
    // Before the rows go rather than after, because afterwards there is nothing
    // left to read the days off. Inside the deletion own transaction, so a
    // store never holds rows whose day has already been taken away from them.
    //
    // A retention deletion reaches none of this. Its statement is that the raw
    // rows have aged out and the figures over them stand, which is what the
    // longer aggregate window exists for: the daily sweep at the default ninety
    // days would otherwise empty aggregates about three hundred days before
    // their own expiry, on every installation running defaults, and take that
    // setting out of service without anybody deciding to remove it.
    private void MoveTheDaysThoseRowsWereIn(
        SqliteTransaction transaction,
        DeletionClass deletionClass,
        string statement,
        Action<SqliteCommand> bind,
        int limit)
    {
        if (deletionClass != DeletionClass.Corrective)
        {
            return;
        }

        var doomed = RollupColumnsOf(transaction, statement, bind, limit);
        if (doomed.Count == 0)
        {
            return;
        }

        for (var i = 0; i < doomed.Count; i++)
        {
            TakeOutOfTheDay(transaction, doomed[i]);
        }

        using var forget = _connection.CreateCommand();
        forget.Transaction = transaction;
        forget.CommandText = ForgetTheDaysThatAreEmpty;
        forget.ExecuteNonQuery();
    }

    // Reads a bounded page of the columns a rollup is made of, into memory
    // before anything is written. One connection cannot be read from and
    // written through at once without the writes landing under the reader, and
    // the page is bounded by the caller own bite, so what is held is one
    // statement worth of rows.
    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "The statement is not composed at run time. It is one of four constants in this file, chosen by the caller because a rebuild and each of the three deletions read the same columns over a different doomed set, and no route from a request, a configuration value or a stored row reaches this parameter. The analyser cannot see that because the statement is data by design: the alternative is the condition assembled in C# that no-sql-built-by-concatenation refuses, which is the defect this shape exists to avoid.")]
    private List<RollupColumns> RollupColumnsOf(
        SqliteTransaction transaction,
        string statement,
        Action<SqliteCommand> bind,
        int limit)
    {
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = statement;
        bind(command);
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<RollupColumns>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RollupColumns
            {
                Id = reader.GetInt64(0),
                StartedUtc = new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
                StoredUserId = reader.GetString(2),
                ItemType = reader.GetString(3),
                ClientName = reader.GetString(4),
                Watched = TimeSpan.FromTicks(reader.GetInt64(5)),
                ReachedTheEnd = reader.GetInt64(6) != 0,
                MethodAtStart = (PlayMethod)reader.GetInt64(7)
            });
        }

        return rows;
    }

    // The eleven values both statements take, bound once. The fold adds them
    // and the mirror subtracts them, and binding them in two places is where
    // the two would start disagreeing about what one play is worth.
    private void BindTheRollupRow(SqliteCommand command, RollupColumns row)
    {
        var delivery = DeliveryOf(row.MethodAtStart);

        command.Parameters.AddWithValue(
            "$day",
            LocalDay.Of(row.StartedUtc, _rollupZone).ToString(DayFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$userId", row.StoredUserId);
        command.Parameters.AddWithValue("$itemType", row.ItemType);
        command.Parameters.AddWithValue("$clientName", row.ClientName);
        command.Parameters.AddWithValue("$watched", row.Watched.Ticks);
        command.Parameters.AddWithValue("$completed", row.ReachedTheEnd ? 1 : 0);
        command.Parameters.AddWithValue("$unknownMethod", delivery.Unknown);
        command.Parameters.AddWithValue("$directPlay", delivery.DirectPlay);
        command.Parameters.AddWithValue("$directStream", delivery.DirectStream);
        command.Parameters.AddWithValue("$transcode", delivery.Transcode);
    }

    /// <summary>
    /// Refuses a deletion class this build has no name for, and hands back the
    /// one it was given.
    /// </summary>
    /// <remarks>
    /// The set has no member at nought, so a caller that passed
    /// <c>default</c> arrives here rather than being recorded as a retention
    /// deletion. That is the one shape the compiler cannot catch: the argument
    /// is required, so omitting it does not build, and what is left is a number
    /// standing where a choice was meant.
    /// <para>
    /// It refuses rather than defaulting, because the two classes say opposite
    /// things about whether a figure over the removed rows still holds, and a
    /// deletion recorded under the wrong one is not repairable afterwards: the
    /// rows it was about are gone.
    /// </para>
    /// </remarks>
    /// <param name="deletionClass">What the caller said the deletion means.</param>
    /// <returns>The same class.</returns>
    private static DeletionClass Declared(DeletionClass deletionClass)
    {
        if (deletionClass is not (DeletionClass.Retention or DeletionClass.Corrective))
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletionClass),
                deletionClass,
                "A deletion says either that the rows aged out or that the plays stop being counted, and this is neither. The two move the figures over those rows in opposite directions, so there is no safe one to assume.");
        }

        return deletionClass;
    }

    /// <summary>
    /// Writes down what a deletion that removed rows said about them, and hands
    /// the count back to the caller.
    /// </summary>
    /// <remarks>
    /// After the rows have gone, so an entry stands for rows that are no longer
    /// in the file rather than for an intention, and only where some went: a
    /// caller bites until a bite comes back empty, so recording the empty one
    /// would end every deletion this plugin performs with an entry saying that
    /// nothing happened.
    /// </remarks>
    /// <param name="transaction">The deletion's own transaction, so the entry and the removal arrive together or neither does.</param>
    /// <param name="rows">How many rows the statement removed.</param>
    /// <param name="deletionClass">What the deletion said about them.</param>
    /// <returns>The same count.</returns>
    private int Recorded(SqliteTransaction transaction, int rows, DeletionClass deletionClass)
    {
        if (rows == 0)
        {
            return 0;
        }

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RecordTheDeletion;
        command.Parameters.AddWithValue("$class", (int)deletionClass);
        command.Parameters.AddWithValue("$rows", rows);
        command.ExecuteNonQuery();

        return rows;
    }

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

    /// <summary>
    /// Reads a moment that may not be there, in UTC.
    /// </summary>
    /// <param name="reader">A reader standing on a row.</param>
    /// <param name="ordinal">Which column.</param>
    /// <returns>The moment, or null where the column holds none.</returns>
    private static DateTime? MomentOrNull(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : Utc(reader.GetInt64(ordinal));

    private static object Number(int? value) => value is null ? DBNull.Value : value.Value;

    // What a rollup row is made of, and no more of a play than that. The
    // account is carried as the text the column holds rather than as an
    // identifier, because both routes into this type already have it in that
    // form, and converting it twice is where a fold and a rebuild would start
    // disagreeing about which row they mean.
    private sealed record RollupColumns
    {
        public required long Id { get; init; }

        public required DateTime StartedUtc { get; init; }

        public required string StoredUserId { get; init; }

        public required string ItemType { get; init; }

        public required string ClientName { get; init; }

        public required TimeSpan Watched { get; init; }

        public required bool ReachedTheEnd { get; init; }

        public required PlayMethod MethodAtStart { get; init; }

        // The identifier is nought here, and it is never read on this route:
        // what a write path folds is the row it is writing, which has no
        // identifier until the insert gives it one. Only the walk that pages by
        // identifier reads it.
        public static RollupColumns Of(PlayRecord play) => new()
        {
            Id = 0,
            StartedUtc = play.StartedUtc,
            StoredUserId = Text(play.UserId),
            ItemType = play.ItemType,
            ClientName = play.ClientName,
            Watched = play.WatchedDuration,
            ReachedTheEnd = play.ReachedTheEnd,
            MethodAtStart = play.PlayMethodAtStart
        };
    }
}
