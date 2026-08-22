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
        }
    ];

    /// <summary>
    /// Gets the version a store is at once every step above has run.
    /// </summary>
    public static int Latest => All[^1].Version;
}
