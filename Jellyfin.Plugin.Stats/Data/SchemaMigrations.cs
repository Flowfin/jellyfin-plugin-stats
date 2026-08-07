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
    /// </remarks>
    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new SchemaMigration
        {
            Version = 1,
            Statements = [CreateThePlaysTable]
        }
    ];

    /// <summary>
    /// Gets the version a store is at once every step above has run.
    /// </summary>
    public static int Latest => All[^1].Version;
}
