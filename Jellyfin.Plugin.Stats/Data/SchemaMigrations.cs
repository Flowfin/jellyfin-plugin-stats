using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Every step this build knows how to take a store through, in order.
/// </summary>
/// <remarks>
/// A version is added by appending to this list and never by editing an entry
/// that has shipped. A store somewhere has already run the old text, and editing
/// it changes what a fresh store gets without changing what an upgraded one has,
/// so the two stop being the same schema while both report the same number.
/// </remarks>
public static class SchemaMigrations
{
    private const string CreateThePlaysTable =
        @"CREATE TABLE IF NOT EXISTS plays (
              Id INTEGER PRIMARY KEY,
              SchemaVersion INTEGER NOT NULL,
              UserId TEXT NOT NULL,
              ItemId TEXT NOT NULL,
              ItemType TEXT NOT NULL,
              ParentId TEXT NULL,
              ItemName TEXT NOT NULL,
              ItemRuntimeTicks INTEGER NULL,
              StartedUtcTicks INTEGER NOT NULL,
              EndedUtcTicks INTEGER NOT NULL,
              WatchedDurationTicks INTEGER NOT NULL,
              ReachedTheEnd INTEGER NOT NULL,
              ClientName TEXT NOT NULL,
              DeviceId TEXT NOT NULL,
              DeviceName TEXT NOT NULL,
              PlayMethod INTEGER NOT NULL,
              TranscodeVideoCodec TEXT NULL,
              TranscodeAudioCodec TEXT NULL,
              TranscodeVideoWasDirect INTEGER NOT NULL,
              TranscodeAudioWasDirect INTEGER NOT NULL,
              TranscodePeakBitrate INTEGER NULL,
              TranscodeTypicalBitrate INTEGER NULL,
              TranscodeHardwareAcceleration TEXT NULL,
              TranscodeReasons TEXT NOT NULL
          )";

    // The table a play sits in while it is still running. Its columns are the
    // finished table's, so a play that stops is the same row moved rather than
    // a second shape somebody has to keep in step, and the key the capture
    // joins a play's events on is the primary key here.
    //
    // No index beside that one. This table holds one row per session that is
    // playing right now, which is a handful on a household server and a few
    // hundred on a large one, so every read and every removal over it is a scan
    // of something that fits in a page. An index here would be a cost nobody
    // measured against a table that never grows with how long the server has
    // been recording.
    private const string CreateTheOpenPlaysTable =
        @"CREATE TABLE IF NOT EXISTS open_plays (
              PlayKey TEXT PRIMARY KEY,
              SchemaVersion INTEGER NOT NULL,
              UserId TEXT NOT NULL,
              ItemId TEXT NOT NULL,
              ItemType TEXT NOT NULL,
              ParentId TEXT NULL,
              ItemName TEXT NOT NULL,
              ItemRuntimeTicks INTEGER NULL,
              StartedUtcTicks INTEGER NOT NULL,
              EndedUtcTicks INTEGER NOT NULL,
              WatchedDurationTicks INTEGER NOT NULL,
              ReachedTheEnd INTEGER NOT NULL,
              ClientName TEXT NOT NULL,
              DeviceId TEXT NOT NULL,
              DeviceName TEXT NOT NULL,
              PlayMethod INTEGER NOT NULL,
              TranscodeVideoCodec TEXT NULL,
              TranscodeAudioCodec TEXT NULL,
              TranscodeVideoWasDirect INTEGER NOT NULL,
              TranscodeAudioWasDirect INTEGER NOT NULL,
              TranscodePeakBitrate INTEGER NULL,
              TranscodeTypicalBitrate INTEGER NULL,
              TranscodeHardwareAcceleration TEXT NULL,
              TranscodeReasons TEXT NOT NULL
          )";

    // One index per shape of read the reports are built on, and no others. Each
    // one is named beside the query it serves in PlayStoreIndexTests, and that
    // suite also refuses an index on this table that no query there names, so an
    // index nobody reads is a red test rather than a cost nobody notices.
    //
    // Every one of them leads with the column its query filters by equality and
    // ends with the started column the same query then ranges over, because that
    // is the order SQLite can use both halves in. The reversed pair serves the
    // range and leaves the equality to a scan.
    private const string IndexPlaysByStart =
        "CREATE INDEX IF NOT EXISTS ix_plays_started ON plays (StartedUtcTicks)";

    private const string IndexPlaysByUserAndStart =
        "CREATE INDEX IF NOT EXISTS ix_plays_user_started ON plays (UserId, StartedUtcTicks)";

    private const string IndexPlaysByItemAndStart =
        "CREATE INDEX IF NOT EXISTS ix_plays_item_started ON plays (ItemId, StartedUtcTicks)";

    private const string IndexPlaysByItemTypeAndStart =
        "CREATE INDEX IF NOT EXISTS ix_plays_item_type_started ON plays (ItemType, StartedUtcTicks)";

    // What issue #158 asks for, on both tables. A row carried two accounts of
    // how a play was delivered under names that read as one answer: a value
    // taken at the start and a summary folded over the whole play. The rename
    // puts the moment in the name, and the column beside it records the moment
    // the two parted company.
    //
    // A rename and an addition rather than a table rebuilt. Both are statements
    // SQLite performs on the table in place, so no row is read, written or
    // discarded, and the rule against a statement that drops a table has
    // nothing to refuse.
    private const string NameThePlayMethodForItsMoment =
        "ALTER TABLE plays RENAME COLUMN PlayMethod TO PlayMethodAtStart";

    private const string RecordWhenTheMethodChanged =
        "ALTER TABLE plays ADD COLUMN PlayMethodChangedUtcTicks INTEGER NULL";

    private const string NameTheRunningPlayMethodForItsMoment =
        "ALTER TABLE open_plays RENAME COLUMN PlayMethod TO PlayMethodAtStart";

    private const string RecordWhenARunningPlaysMethodChanged =
        "ALTER TABLE open_plays ADD COLUMN PlayMethodChangedUtcTicks INTEGER NULL";

    // What issue #222 asks for, on both tables. A play the server sent a stop
    // for and a play something gave up waiting for were the same row, so a
    // report could say how many plays it had read and not how many of them
    // ended cleanly.
    //
    // Null on every row that is already there, and null reads back as the row
    // not saying rather than as a clean ending. That is the honest answer:
    // nothing was recording a route when those rows were written, and a column
    // defaulted to the clean value would turn a gap in the record into a claim
    // about what happened.
    //
    // The open table gets it as well, because one function reads a row out of
    // either table and a column on one of them is a column on both. A running
    // play has not been closed, so what it carries there is the not-said value
    // until it is.
    private const string RecordWhatClosedAPlay =
        "ALTER TABLE plays ADD COLUMN ClosedBy INTEGER NULL";

    private const string RecordWhatWillCloseARunningPlay =
        "ALTER TABLE open_plays ADD COLUMN ClosedBy INTEGER NULL";

    // What issue #40 asks for, on both tables. A live television play has no
    // programme length worth a completion ratio and no title a report can group
    // on twice, and what it does have is the channel it was on. The name and
    // not the identifier: a live channel is renamed and taken off the air, and
    // the rows a yearly report is about are exactly the old ones, so an
    // identifier stored alone becomes unnameable with time.
    //
    // Null on every row already on the file, and null on every row that is not
    // live television, which reads back as the row naming no channel. There is
    // no value that could stand for "was live television and the channel is
    // unknown" without a report being able to print it as a channel.
    //
    // An addition rather than a rebuild, so no row is read, written or
    // discarded, and the open table gets it for the reason the two before it
    // did: one function reads a row out of either table, so a column on one is
    // a column on both.
    private const string RecordTheChannelALivePlayWasOn =
        "ALTER TABLE plays ADD COLUMN ChannelName TEXT NULL";

    private const string RecordTheChannelARunningLivePlayIsOn =
        "ALTER TABLE open_plays ADD COLUMN ChannelName TEXT NULL";

    // What one account has said about being named in the views this plugin
    // draws. One row per account and the account is the key, because the
    // question has one answer at a time and what a reader asks is what that
    // answer is now.
    //
    // The moments are kept apart rather than folded into one. An account that
    // agreed in March and withdrew in July has said two things, and a column
    // holding only the last of them cannot answer for the months between.
    //
    // No index beside the key. This table holds one row per person on the
    // server, and every read of it is by that key.
    private const string CreateTheConsentsTable =
        @"CREATE TABLE IF NOT EXISTS consents (
              UserId TEXT PRIMARY KEY,
              Agreed INTEGER NOT NULL,
              AgreedUtcTicks INTEGER NULL,
              WithdrawnUtcTicks INTEGER NULL,
              WordingVersion INTEGER NOT NULL
          )";

    // The day-by-day account of what was played, folded as rows are written.
    //
    // Every column is one a play row carries or one that follows from the play
    // rows alone. That is not a preference: the rebuild in issue #253 has to be
    // able to produce this row again from the rows underneath it, so a figure
    // that could not be derived from them would make this table the only record
    // of something, and the only record of something cannot be rebuilt.
    //
    // The key is the four things a report groups by. A day, an account, a kind
    // of item and a client name together name one row, so a play folds into the
    // row it belongs to and no reader has to add rows up to answer about one of
    // them.
    //
    // The day is TEXT and not a number of ticks, because it is a calendar day
    // rather than a moment: a day has no single length, so the instant that
    // would stand for it is a second fact somebody would have to convert back.
    // Written as an ISO date, which sorts as text in the order it sorts as a
    // date, so a range over days is a range over this column.
    //
    // Which day a play falls on depends on whose midnight is meant, so the zone
    // these days were counted in is stated at the table below rather than
    // assumed by each reader.
    //
    // Four delivery counts rather than the two a reader usually wants. The fold
    // this stands beside distinguishes four, so folding them to two here would
    // fold away the difference between a play the server repackaged and one it
    // re-encoded, and would leave a play whose method was never reported with
    // nowhere to go. Transcoded is one column and direct is the sum of two.
    private const string CreateTheDailyRollupsTable =
        @"CREATE TABLE IF NOT EXISTS daily_rollups (
              Day TEXT NOT NULL,
              UserId TEXT NOT NULL,
              ItemType TEXT NOT NULL,
              ClientName TEXT NOT NULL,
              Plays INTEGER NOT NULL,
              WatchedDurationTicks INTEGER NOT NULL,
              Completed INTEGER NOT NULL,
              UnknownMethod INTEGER NOT NULL,
              DirectPlay INTEGER NOT NULL,
              DirectStream INTEGER NOT NULL,
              Transcode INTEGER NOT NULL,
              PRIMARY KEY (Day, UserId, ItemType, ClientName)
          )";

    // The zone the days above were counted in, stated once for the whole table.
    //
    // A column on every rollup row was the alternative and it says something
    // different: it would let one file hold two answers for one day, one keyed
    // in each of two zones, which is a store nothing can read as a day. One row
    // here says the table has one meaning, and a store whose setting has moved
    // since is a store whose rollups have to be rebuilt rather than one whose
    // rows quietly change what they mean.
    //
    // Shaped like the version table beside it: no key, one row, read with a
    // limit. Nothing here fills it, because a migration cannot know what the
    // running configuration says. The store writes it the first time it keys a
    // rollup, and until then the table is empty and states nothing.
    private const string CreateTheRollupZoneTable =
        "CREATE TABLE IF NOT EXISTS rollup_zone (ZoneId TEXT NOT NULL)";

    // What a report over one account's year reads through. Issue #254.
    //
    // The table's own key leads with the day, which serves a range over days for
    // everybody and serves one account's range badly: the planner walks every
    // account's rows inside the range and discards the ones that are not the
    // caller's, and it cannot take the order of the last two columns from a key
    // it is ranging over, so it sorts afterwards as well. A wrap-up is always
    // about one account, so the account leads here and the day ranges after it.
    //
    // The last two columns are on the end for the order rather than for the
    // search. With the account fixed and the days ranged over, an index ending
    // where the query orders is the difference between handing rows back as
    // they are read and holding the whole year before the first one.
    private const string IndexRollupsByAccountAndDay =
        "CREATE INDEX IF NOT EXISTS ix_rollups_user_day ON daily_rollups (UserId, Day, ItemType, ClientName)";

    // What each deletion said about the rows it took. Issue #251.
    //
    // A deletion removes rows and leaves nothing behind, so a reader arriving
    // afterwards sees a gap and cannot tell a window that aged out from plays
    // somebody asked to stop counting. That difference decides whether a figure
    // standing over those rows still holds, and it is knowable only at the
    // moment of the deletion. This is where it is kept.
    //
    // Append-only and never read by row identity, so the key is the order the
    // entries were written in and there is no other index. A read of this table
    // is newest first over that key, which the primary key already orders.
    //
    // No moment. The store names no clock, and a moment read off the machine
    // inside a deletion would be a second fact about the run rather than about
    // the deletion, disagreeing with the row timestamps on a server whose clock
    // has moved. What a later reader needs from this table is which class each
    // deletion was and in what order, and the key answers the order.
    private const string CreateTheDeletionsTable =
        @"CREATE TABLE IF NOT EXISTS deletions (
              Id INTEGER PRIMARY KEY,
              Class INTEGER NOT NULL,
              Rows INTEGER NOT NULL
          )";

    /// <summary>
    /// Gets the steps, in the order they are applied.
    /// </summary>
    /// <remarks>
    /// The first step creates the plays table if it is not already there. The
    /// conditional is not decoration: a store written by the build that added
    /// the table before there was any versioning carries the table and no
    /// version, so it arrives here reading as version zero with its rows in
    /// place. Running this step over it records the version it was already at
    /// and leaves every row alone, which is why that store needs no case of its
    /// own anywhere in the runner.
    /// <para>
    /// The second step is the index set the reports read through. It is a step
    /// of its own rather than four lines added to the first, because the first
    /// has shipped: a store somewhere has already run it, and editing a step
    /// that has run changes what a fresh store gets without changing what an
    /// upgraded one has. Adding an index to a table that already has rows
    /// builds over those rows and moves none of them.
    /// </para>
    /// <para>
    /// The third step is the table a play sits in while it is running, and it
    /// is appended for the same reason. A store from an earlier build arrives
    /// with its finished rows and without this table, gets it, and keeps every
    /// row it had, because creating a table beside another one reads nothing
    /// and moves nothing.
    /// </para>
    /// <para>
    /// The fourth names the delivery method for the moment it is about and adds
    /// the moment that method changed. A rename keeps every value where it was
    /// under a name that says which moment it speaks about, and a column added
    /// to a table that has rows is null on all of them, which is the honest
    /// answer for a play written before anything was watching for the change.
    /// </para>
    /// <para>
    /// The fifth is the table of what each account has said about being named,
    /// appended beside the others for the same reason the third was: creating a
    /// table reads nothing and moves nothing, so a store from any earlier build
    /// arrives with every row it had.
    /// </para>
    /// <para>
    /// The sixth records which route ended a play, on both tables. It is an
    /// addition and not a rebuild, so no row is read, written or discarded, and
    /// every row already on the file is null there, which reads back as the row
    /// not saying. That is issue #222's second condition and it is what the
    /// column being nullable buys: a default of the clean value would have
    /// turned every row a previous build wrote into a claim that the server sent
    /// a stop for it.
    /// </para>
    /// <para>
    /// The seventh records the channel a live television play was on, on both
    /// tables, and it is an addition for the same reason the sixth was. Every
    /// row already on the file is null there, and so is every row this build
    /// writes for a play that was not live television, so what the column says
    /// is that this row names no channel rather than that the channel was
    /// called nothing. That is issue #40's remaining sentence.
    /// </para>
    /// <para>
    /// The eighth is the day-by-day account and the zone its days are counted
    /// in, two tables created beside the others. A store from any earlier build
    /// arrives with every row it had, because creating a table reads nothing and
    /// moves nothing, and it arrives with both tables empty. The rollups a store
    /// already holding plays is owed are what issue #253's rebuild is for: a step
    /// that folded them here would be reading every row on the file inside a
    /// migration, which is work no upgrade can bound.
    /// </para>
    /// <para>
    /// The ninth is what each deletion said about the rows it removed, a table
    /// created beside the others, so a store from any earlier build arrives with
    /// every row it had and with this table empty. Empty is the honest state for
    /// it: the deletions such a store has already performed were made by builds
    /// that recorded no class, and a migration writing a class for them would be
    /// inventing the answer this table exists to stop being guessed. That is
    /// issue #251, and it is why the table lands before the first figure is
    /// computed from the rows rather than with it.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new SchemaMigration
        {
            Version = 1,
            Statements = [CreateThePlaysTable]
        },
        new SchemaMigration
        {
            Version = 2,
            Statements =
            [
                IndexPlaysByStart,
                IndexPlaysByUserAndStart,
                IndexPlaysByItemAndStart,
                IndexPlaysByItemTypeAndStart
            ]
        },
        new SchemaMigration
        {
            Version = 3,
            Statements = [CreateTheOpenPlaysTable]
        },
        new SchemaMigration
        {
            Version = 4,
            Statements =
            [
                NameThePlayMethodForItsMoment,
                RecordWhenTheMethodChanged,
                NameTheRunningPlayMethodForItsMoment,
                RecordWhenARunningPlaysMethodChanged
            ]
        },
        new SchemaMigration
        {
            Version = 5,
            Statements = [CreateTheConsentsTable]
        },
        new SchemaMigration
        {
            Version = 6,
            Statements =
            [
                RecordWhatClosedAPlay,
                RecordWhatWillCloseARunningPlay
            ]
        },
        new SchemaMigration
        {
            Version = 7,
            Statements =
            [
                RecordTheChannelALivePlayWasOn,
                RecordTheChannelARunningLivePlayIsOn
            ]
        },
        new SchemaMigration
        {
            Version = 8,
            Statements =
            [
                CreateTheDailyRollupsTable,
                CreateTheRollupZoneTable
            ]
        },
        new SchemaMigration
        {
            Version = 9,
            Statements = [CreateTheDeletionsTable]
        },
        new SchemaMigration
        {
            Version = 10,
            Statements = [IndexRollupsByAccountAndDay]
        }
    ];

    /// <summary>
    /// Gets the version a store is at once every step above has run.
    /// </summary>
    public static int Latest => All[^1].Version;
}
