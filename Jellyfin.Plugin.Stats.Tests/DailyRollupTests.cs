// The day-by-day account, built as rows are written. Issue #252.
//
// Everything here is asserted over the store rather than over a fold. The point
// of the table is that a figure survives the request that produced it, so a case
// that checked what a fold returned would pass over a store that wrote nothing,
// and that is the failure this table exists to make impossible.
//
// The zone is chosen by each case and nothing reads a clock or a setting. Berlin
// is the one used where a day boundary matters, because a play at eleven at
// night there belongs to the next day in UTC, so a case that fell back to UTC
// would come out one day off rather than passing quietly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class DailyRollupTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private static readonly Guid Bob = new("22222222222222222222222222222222");

    private static readonly DateTime March =
        new(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public DailyRollupTests()
    {
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
    /// The first condition of issue #252. The table arrives as an appended step,
    /// and a store written by a build that did not have it opens afterwards with
    /// the table and with every row it had.
    /// </summary>
    /// <remarks>
    /// The step list is asserted as well as the upgrade, the way the two
    /// migrations before this one are. A table added by editing a step that has
    /// already shipped would leave the upgrade half of this case green and would
    /// leave every installation that had already run that step without the
    /// table.
    /// </remarks>
    [Fact]
    public void TheTablesArriveAsAnAppendedStepAndAnOlderStoreStillReads()
    {
        Assert.Equal(10, SchemaMigrations.Latest);
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
            SchemaMigrations.All.Select(step => step.Version));

        Directory.CreateDirectory(_root);

        using (var connection = OpenTheFile())
        {
            // A store at the version before this one, with a row in it, which is
            // what an installation that has been recording for months looks like
            // on the day it upgrades.
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 7).ToList());

            Assert.False(TableExists(connection, "daily_rollups"));
            Assert.False(TableExists(connection, "rollup_zone"));
        }

        using (var before = new SqlitePlayStore(_root, Berlin))
        {
            before.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true));
        }

        using var after = new SqlitePlayStore(_root, Berlin);

        Assert.Single(after.AllPlays());

        using var reopened = OpenTheFile();

        Assert.True(TableExists(reopened, "daily_rollups"));
        Assert.True(TableExists(reopened, "rollup_zone"));
    }

    /// <summary>
    /// A store that already held plays before the table arrived gets the table
    /// and none of the days those plays fall on.
    /// </summary>
    /// <remarks>
    /// The honest answer and not an oversight, and it is asserted so that
    /// nobody reads the table as complete on such a store. Folding those rows
    /// inside a migration would be reading every row on the file inside a step
    /// no upgrade can bound, and producing them afterwards is the rebuild in
    /// #253.
    /// </remarks>
    [Fact]
    public void AnUpgradedStoreGetsTheTableAndNotTheDaysItAlreadyHad()
    {
        Directory.CreateDirectory(_root);

        using (var connection = OpenTheFile())
        {
            SchemaMigrator.MigrateToLatest(
                connection,
                SchemaMigrations.All.Where(step => step.Version <= 7).ToList());

            InsertAPlayTheOldWay(connection, Ada, March);
        }

        using var after = new SqlitePlayStore(_root, Berlin);

        Assert.Single(after.AllPlays());
        Assert.Empty(after.AllRollups());
    }

    /// <summary>
    /// The second condition of issue #252. A play written through the write path
    /// moves the row covering its day, account, kind of item and client.
    /// </summary>
    [Fact]
    public void APlayMovesTheRowCoveringItsDayAccountItemTypeAndClient()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true));
            store.Add(APlay(Ada, March.AddHours(2), TimeSpan.FromMinutes(10), reachedTheEnd: false));
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        var row = Assert.Single(reopened.AllRollups());

        Assert.Equal(new DateOnly(2026, 3, 4), row.Day);
        Assert.Equal(Ada, row.UserId);
        Assert.Equal("Episode", row.ItemType);
        Assert.Equal("Jellyfin Web", row.ClientName);
        Assert.Equal(2, row.Plays);
        Assert.Equal(TimeSpan.FromMinutes(30), row.Watched);
        Assert.Equal(1, row.Completed);
        Assert.Equal(2, row.DirectPlay);
    }

    /// <summary>
    /// Four things separate one row from another, and each of them on its own
    /// does.
    /// </summary>
    /// <remarks>
    /// One case rather than four, because what is being asserted is that the key
    /// is the whole of the four: a key missing any one of them would fold two of
    /// these five plays together and the count would be four rows rather than
    /// five.
    /// </remarks>
    [Fact]
    public void EachPartOfTheKeySeparatesARowFromItsNeighbour()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true));
            store.Add(APlay(Ada, March.AddDays(1), TimeSpan.FromMinutes(20), reachedTheEnd: true));
            store.Add(APlay(Bob, March, TimeSpan.FromMinutes(20), reachedTheEnd: true));
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true, itemType: "Movie"));
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true, clientName: "Android TV"));
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        Assert.Equal(5, reopened.AllRollups().Count());
        Assert.All(reopened.AllRollups(), row => Assert.Equal(1, row.Plays));
    }

    /// <summary>
    /// The four delivery counts add up to the play count, whichever methods the
    /// plays behind a row started with.
    /// </summary>
    /// <remarks>
    /// A play whose method was never reported is one of the four rather than
    /// nothing, which is what makes the addition hold. A table holding only the
    /// transcoded and direct counts would have dropped it, and a reader adding
    /// what was there would have got a number smaller than the plays with
    /// nothing saying why.
    /// </remarks>
    [Fact]
    public void EveryPlayLandsInExactlyOneOfTheFourDeliveryCounts()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true, method: PlayMethod.DirectPlay));
            store.Add(APlay(Ada, March.AddHours(1), TimeSpan.FromMinutes(20), reachedTheEnd: true, method: PlayMethod.DirectStream));
            store.Add(APlay(Ada, March.AddHours(2), TimeSpan.FromMinutes(20), reachedTheEnd: true, method: PlayMethod.Transcode));
            store.Add(APlay(Ada, March.AddHours(3), TimeSpan.FromMinutes(20), reachedTheEnd: true, method: PlayMethod.Unknown));
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        var row = Assert.Single(reopened.AllRollups());

        Assert.Equal(4, row.Plays);
        Assert.Equal(1, row.UnknownMethod);
        Assert.Equal(1, row.DirectPlay);
        Assert.Equal(1, row.DirectStream);
        Assert.Equal(1, row.Transcode);
        Assert.Equal(
            row.Plays,
            row.UnknownMethod + row.DirectPlay + row.DirectStream + row.Transcode);
    }

    /// <summary>
    /// The third condition of issue #252. Every figure in the table is one the
    /// play rows produce, checked by producing them a second way.
    /// </summary>
    /// <remarks>
    /// This is the comparison #253's rebuild will make, made here against the
    /// incremental build while there is nothing else to compare it with. It
    /// walks the rows through the read that answers to nobody's filter and folds
    /// them the way this issue words each column, so a column that could only
    /// have come from somewhere other than a play row has nothing to agree with.
    /// </remarks>
    [Fact]
    public void EveryFigureInTheTableIsOneThePlayRowsProduce()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true, method: PlayMethod.Transcode));
            store.Add(APlay(Ada, March.AddHours(1), TimeSpan.FromMinutes(5), reachedTheEnd: false));
            store.Add(APlay(Bob, March.AddDays(2), TimeSpan.FromMinutes(41), reachedTheEnd: true, itemType: "Movie"));
            store.Add(APlay(Ada, March.AddDays(2), TimeSpan.FromMinutes(9), reachedTheEnd: false, clientName: "Android TV", method: PlayMethod.DirectStream));
            store.Add(APlay(Bob, March.AddHours(23), TimeSpan.FromMinutes(12), reachedTheEnd: true, method: PlayMethod.Unknown));
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        var zone = Assert.IsType<TimeZoneInfo>(reopened.RollupZone);

        Assert.Equal(
            FoldedFromTheRows(reopened.AllPlays(), zone),
            reopened.AllRollups().ToList());
    }

    /// <summary>
    /// A day is a local day, and the store keys it in the zone it states rather
    /// than in the one the machine is set to.
    /// </summary>
    /// <remarks>
    /// The play here starts at ten at night UTC, which is eleven at night in
    /// Berlin on the same date and midnight in Auckland on the next one. The two
    /// stores are two files, so what is asserted is the keying and not what one
    /// file does when the setting under it moves, which is the case below.
    /// </remarks>
    [Fact]
    public void TheDayIsTheLocalDayOfTheZoneTheStoreStates()
    {
        var lateAtNight = new DateTime(2026, 3, 4, 22, 0, 0, DateTimeKind.Utc);
        var auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

        Assert.Equal(new DateOnly(2026, 3, 4), OneDayIn(Berlin, "berlin", lateAtNight));
        Assert.Equal(new DateOnly(2026, 3, 5), OneDayIn(auckland, "auckland", lateAtNight));
    }

    /// <summary>
    /// A store states the zone it first kept a rollup in and goes on stating it,
    /// whatever the process is configured with afterwards.
    /// </summary>
    /// <remarks>
    /// The alternative is worse than it looks. A store that re-keyed on every
    /// open would hold days counted in one zone under a table claiming another,
    /// and no reader could tell which rows were which, so a setting moved once
    /// would corrupt every day already folded rather than changing what happens
    /// next. Rekeying is a rebuild, which is #253, and until then this is the
    /// property that stops the file quietly changing what it means.
    /// </remarks>
    [Fact]
    public void AStoreKeepsTheZoneItStatedWhenTheSettingMovesUnderIt()
    {
        var lateAtNight = new DateTime(2026, 3, 4, 22, 0, 0, DateTimeKind.Utc);
        var auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

        using (var first = new SqlitePlayStore(_root, Berlin))
        {
            first.Add(APlay(Ada, lateAtNight, TimeSpan.FromMinutes(20), reachedTheEnd: true));
        }

        using var second = new SqlitePlayStore(_root, auckland);

        Assert.Equal(Berlin.Id, Assert.IsType<TimeZoneInfo>(second.RollupZone).Id);

        second.Add(APlay(Bob, lateAtNight, TimeSpan.FromMinutes(20), reachedTheEnd: true));

        Assert.All(second.AllRollups(), row => Assert.Equal(new DateOnly(2026, 3, 4), row.Day));
    }

    /// <summary>
    /// A store opened with no opinion about the zone keys its days in the one
    /// the configuration model falls back to.
    /// </summary>
    [Fact]
    public void AStoreOpenedWithNoZoneStatesTheDefaultOne()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Equal(
            TimeZoneInfo.FindSystemTimeZoneById(Configuration.ConfigurationLimits.DefaultRollupTimeZone).Id,
            Assert.IsType<TimeZoneInfo>(store.RollupZone).Id);
    }

    /// <summary>
    /// Every route that writes a finished play folds it, and not only the one a
    /// case would reach for first.
    /// </summary>
    /// <remarks>
    /// Three routes write a finished row: the single write, the bulk write, and
    /// the write that finishes a play that was running. A rollup built on one of
    /// them and not the others is a table that is right on a test and wrong on a
    /// server, because the route a real play takes is the third.
    /// </remarks>
    [Fact]
    public void EveryRouteThatWritesAFinishedPlayFoldsIt()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            store.Add(APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true));

            store.AddMany([APlay(Ada, March.AddHours(1), TimeSpan.FromMinutes(20), reachedTheEnd: true)]);

            store.NoteOpenPlay(new OpenPlay
            {
                PlayKey = "play-1",
                SoFar = APlay(Ada, March.AddHours(2), TimeSpan.FromMinutes(1), reachedTheEnd: false)
            });

            store.AddAndForgetOpenPlay(
                APlay(Ada, March.AddHours(2), TimeSpan.FromMinutes(20), reachedTheEnd: true),
                "play-1");
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        var row = Assert.Single(reopened.AllRollups());

        Assert.Equal(3, row.Plays);
        Assert.Equal(TimeSpan.FromMinutes(60), row.Watched);
    }

    /// <summary>
    /// The row and the day it moves are one fact. A write that stops partway
    /// leaves neither of them.
    /// </summary>
    /// <remarks>
    /// Driven through the bulk write, because that is the only route on which a
    /// failure can land AFTER a row and its day have been written. Every refusal
    /// the single write can produce happens while the row is being bound, before
    /// either statement runs, so a case driving that route would come out empty
    /// whether the two shared a transaction or not and would be asserting
    /// nothing. THE SINGLE WRITE IS THEREFORE NOT SEPARATELY PROVED HERE, and
    /// that is a gap rather than a claim: it holds by carrying the same
    /// transaction as this one, which is read rather than executed.
    /// <para>
    /// What this does assert is the pairing, over the route that can break it. A
    /// good play goes in first and moves its day; a play the store will not take
    /// follows it; and afterwards there is neither a row nor a figure standing
    /// over one. A rollup that gained a play whose row went back is a figure
    /// nobody can find the rows behind, and a rebuild comparing the table
    /// against the rows would report it as a defect in the rebuild.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWriteThatStopsPartwayMovesNoDay()
    {
        using (var store = new SqlitePlayStore(_root, Berlin))
        {
            Assert.ThrowsAny<Exception>(() => store.AddMany(
            [
                APlay(Ada, March, TimeSpan.FromMinutes(20), reachedTheEnd: true),
                APlayTheStoreWillNotTake(Ada, March.AddHours(1))
            ]));
        }

        using var reopened = new SqlitePlayStore(_root, Berlin);

        Assert.Empty(reopened.AllPlays());
        Assert.Empty(reopened.AllRollups());
    }

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan watched,
        bool reachedTheEnd,
        string itemType = "Episode",
        string clientName = "Jellyfin Web",
        PlayMethod method = PlayMethod.DirectPlay,
        string[]? reasons = null)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = new Guid("33333333333333333333333333333333"),
            ItemType = itemType,
            ParentId = null,
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = reachedTheEnd,
            ClientName = clientName,
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
                Reasons = reasons ?? Array.Empty<string>()
            }
        };

    // A transcode reason carrying the character the store separates reasons
    // with, which the write refuses at the last moment the original is still
    // there to refuse.
    private static PlayRecord APlayTheStoreWillNotTake(Guid userId, DateTime startedUtc)
        => APlay(userId, startedUtc, TimeSpan.FromMinutes(20), reachedTheEnd: true, reasons: ["one|two"]);

    /// <summary>
    /// The same figures, folded out of the play rows the way this issue words
    /// each column. Nothing here reads the rollup table.
    /// </summary>
    private static List<DailyRollup> FoldedFromTheRows(IEnumerable<PlayRecord> plays, TimeZoneInfo zone)
    {
        var byKey = new Dictionary<(DateOnly Day, Guid UserId, string ItemType, string ClientName), DailyRollup>();

        foreach (var play in plays)
        {
            var key = (
                Jellyfin.Plugin.Stats.Aggregation.LocalDay.Of(play.StartedUtc, zone),
                play.UserId,
                play.ItemType,
                play.ClientName);

            byKey.TryGetValue(key, out var so_far);

            byKey[key] = new DailyRollup
            {
                Day = key.Item1,
                UserId = key.UserId,
                ItemType = key.ItemType,
                ClientName = key.ClientName,
                Plays = (so_far?.Plays ?? 0) + 1,
                Watched = (so_far?.Watched ?? TimeSpan.Zero) + play.WatchedDuration,
                Completed = (so_far?.Completed ?? 0) + (play.ReachedTheEnd ? 1 : 0),
                UnknownMethod = (so_far?.UnknownMethod ?? 0) + (play.PlayMethodAtStart == PlayMethod.Unknown ? 1 : 0),
                DirectPlay = (so_far?.DirectPlay ?? 0) + (play.PlayMethodAtStart == PlayMethod.DirectPlay ? 1 : 0),
                DirectStream = (so_far?.DirectStream ?? 0) + (play.PlayMethodAtStart == PlayMethod.DirectStream ? 1 : 0),
                Transcode = (so_far?.Transcode ?? 0) + (play.PlayMethodAtStart == PlayMethod.Transcode ? 1 : 0)
            };
        }

        return byKey.Values
            .OrderBy(row => row.Day)
            .ThenBy(row => row.UserId.ToString("N", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .ThenBy(row => row.ItemType, StringComparer.Ordinal)
            .ThenBy(row => row.ClientName, StringComparer.Ordinal)
            .ToList();
    }

    private static bool TableExists(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", name);

        return (long)command.ExecuteScalar()! > 0;
    }

    // A row written the way the build before the rollups wrote one, so the case
    // above meets a store with rows and no days rather than one this build
    // filled.
    private static void InsertAPlayTheOldWay(SqliteConnection connection, Guid userId, DateTime startedUtc)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            @"INSERT INTO plays (
                  SchemaVersion, UserId, ItemId, ItemType, ParentId, ItemName, ItemRuntimeTicks,
                  StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                  ClientName, DeviceId, DeviceName, PlayMethodAtStart, PlayMethodChangedUtcTicks,
                  TranscodeVideoCodec, TranscodeAudioCodec, TranscodeVideoWasDirect, TranscodeAudioWasDirect,
                  TranscodePeakBitrate, TranscodeTypicalBitrate, TranscodeHardwareAcceleration, TranscodeReasons,
                  ClosedBy, ChannelName
              )
              VALUES (
                  7, $userId, $itemId, 'Episode', NULL, 'Something', NULL,
                  $started, $started, 0, 1,
                  'Jellyfin Web', 'device-1', 'A browser', 1, NULL,
                  NULL, NULL, 1, 1,
                  NULL, NULL, NULL, '',
                  NULL, NULL
              )";
        command.Parameters.AddWithValue("$userId", userId.ToString("N", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$itemId", Guid.Empty.ToString("N", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$started", startedUtc.Ticks);
        command.ExecuteNonQuery();
    }

    private DateOnly OneDayIn(TimeZoneInfo zone, string folder, DateTime startedUtc)
    {
        var root = Path.Combine(_root, folder);

        using var store = new SqlitePlayStore(root, zone);

        store.Add(APlay(Ada, startedUtc, TimeSpan.FromMinutes(20), reachedTheEnd: true));

        return Assert.Single(store.AllRollups()).Day;
    }

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
}
