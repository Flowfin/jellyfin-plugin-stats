// The open table, driven at the store rather than through the capture path.
//
// What the path proves is that a running play reaches the file at all. What
// this proves is the shape of the table underneath it: that a key is a row,
// that a stop moves one row from one table to the other rather than adding to
// both, and that every removal this plugin has reaches a running play as well
// as a finished one. That last group is the one worth having a file for: an
// open row holds the same account and the same item name a finished row does,
// so a deletion that took one and left the other would answer a request to be
// forgotten with the rows still there. Issue #220.

using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class OpenPlayStoreTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public OpenPlayStoreTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// A running play comes back with every field it went in with, read
    /// through a store opened again over the same file.
    /// </summary>
    [Fact]
    public void ARunningPlayComesBackAsItWentIn()
    {
        var play = ARunningPlay("play-1", Alice);

        using (var store = new SqlitePlayStore(_root))
        {
            store.NoteOpenPlay(play);
        }

        using var reopened = new SqlitePlayStore(_root);

        Assert.Equal(play, Assert.Single(reopened.OpenPlays()));
    }

    /// <summary>
    /// The key is the row. Writing the same key again leaves one row carrying
    /// the later values, which is what keeps a play's cost off how often its
    /// session reported.
    /// </summary>
    [Fact]
    public void WritingTheSameKeyAgainLeavesOneRow()
    {
        using var store = new SqlitePlayStore(_root);

        for (var minute = 1; minute <= 50; minute++)
        {
            store.NoteOpenPlay(ARunningPlay("play-1", Alice, watched: TimeSpan.FromMinutes(minute)));
        }

        var running = Assert.Single(store.OpenPlays());

        Assert.Equal(TimeSpan.FromMinutes(50), running.SoFar.WatchedDuration);
    }

    /// <summary>
    /// Two plays are two rows. A key that stood for the store rather than for
    /// one play would pass the case above and lose every play but the last.
    /// </summary>
    [Fact]
    public void TwoRunningPlaysAreTwoRows()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("play-1", Alice));
        store.NoteOpenPlay(ARunningPlay("play-2", Bob));

        Assert.Equal(new[] { "play-1", "play-2" }, store.OpenPlays().Select(play => play.PlayKey));
    }

    /// <summary>
    /// A stop is one row moving rather than a row added. Both tables are
    /// counted, because either one alone would pass over the failure the other
    /// names.
    /// </summary>
    [Fact]
    public void AStopLeavesTheFinishedRowAndTakesTheRunningOne()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("play-1", Alice));
        store.AddAndForgetOpenPlay(AFinishedPlay(Alice), "play-1");

        Assert.Single(store.AllPlays());
        Assert.Empty(store.OpenPlays());
    }

    /// <summary>
    /// A stop for a key with no running row against it still keeps the play. A
    /// play whose start this plugin never saw, and one whose running row a
    /// sweep has already taken, both arrive that way.
    /// </summary>
    [Fact]
    public void AStopWithNoRunningRowStillKeepsThePlay()
    {
        using var store = new SqlitePlayStore(_root);

        store.AddAndForgetOpenPlay(AFinishedPlay(Alice), "a-key-nothing-was-written-under");

        Assert.Single(store.AllPlays());
        Assert.Empty(store.OpenPlays());
    }

    /// <summary>
    /// A running row can be taken away with no finished play written, which is
    /// what a play that is no longer being recorded leaves behind.
    /// </summary>
    [Fact]
    public void ARunningRowCanBeTakenAwayOnItsOwn()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("play-1", Alice));
        store.NoteOpenPlay(ARunningPlay("play-2", Bob));
        store.ForgetOpenPlay("play-1");

        Assert.Equal("play-2", Assert.Single(store.OpenPlays()).PlayKey);
        Assert.Empty(store.AllPlays());
    }

    /// <summary>
    /// Deleting an account's rows takes its running play as well. It holds the
    /// same identifier and the same item name a finished row does, so leaving
    /// it would answer a deletion with the data still there.
    /// </summary>
    [Fact]
    public void DeletingAnAccountTakesItsRunningPlay()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("play-1", Alice));
        store.NoteOpenPlay(ARunningPlay("play-2", Bob));

        store.DeletePlaysFor(Alice, 100);

        Assert.Equal("play-2", Assert.Single(store.OpenPlays()).PlayKey);
    }

    /// <summary>
    /// The same, for a deletion the account asked for over a window. A running
    /// play that started inside it goes and one that started outside stays.
    /// </summary>
    [Fact]
    public void DeletingAWindowTakesTheRunningPlaysThatStartedInIt()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("inside", Alice, startedUtc: March));
        store.NoteOpenPlay(ARunningPlay("outside", Alice, startedUtc: March.AddDays(1)));

        store.DeletePlaysFor(Alice, March, March.AddHours(1), 100);

        Assert.Equal("outside", Assert.Single(store.OpenPlays()).PlayKey);
    }

    /// <summary>
    /// The retention sweep takes a running play that started before the
    /// cutoff. A play older than the window that is still marked as running is
    /// a leftover rather than a session anybody is watching.
    /// </summary>
    [Fact]
    public void TheRetentionCutoffTakesRunningPlaysOlderThanIt()
    {
        using var store = new SqlitePlayStore(_root);

        store.NoteOpenPlay(ARunningPlay("old", Alice, startedUtc: March));
        store.NoteOpenPlay(ARunningPlay("recent", Alice, startedUtc: March.AddDays(2)));

        store.DeletePlaysStartedBefore(March.AddDays(1), 100);

        Assert.Equal("recent", Assert.Single(store.OpenPlays()).PlayKey);
    }

    /// <summary>
    /// The store refuses to be asked about nothing, on each of the three
    /// members that take a running play or its key.
    /// </summary>
    [Fact]
    public void TheStoreRefusesARunningPlayItCannotWrite()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentNullException>(() => store.NoteOpenPlay(null!));
        Assert.Throws<ArgumentException>(() => store.NoteOpenPlay(ARunningPlay(string.Empty, Alice)));
        Assert.Throws<ArgumentNullException>(() => store.ForgetOpenPlay(null!));
        Assert.Throws<ArgumentException>(() => store.ForgetOpenPlay(string.Empty));
        Assert.Throws<ArgumentNullException>(() => store.AddAndForgetOpenPlay(null!, "play-1"));
        Assert.Throws<ArgumentException>(() => store.AddAndForgetOpenPlay(AFinishedPlay(Alice), string.Empty));
    }

    /// <summary>
    /// A store written by the build before this table existed gets it and keeps
    /// every row it had. The step is appended rather than edited, so an
    /// installation arrives here by replaying what it missed.
    /// </summary>
    [Fact]
    public void AStoreFromTheBuildBeforeTheRunningTableGetsItAndKeepsItsRows()
    {
        // Built by running the steps the earlier build knew and no more, which
        // is the state that build left a file in. Derived from the step list
        // rather than from a fixture or from undoing the newest step, so it is
        // the earlier shape rather than somebody's recollection of it.
        Directory.CreateDirectory(_root);

        var earlier = SchemaMigrations.All.Where(step => step.Version <= 2).ToList();

        using (var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Join(_root, SqlitePlayStore.FileName),
                Pooling = false
            }.ToString()))
        {
            connection.Open();

            Assert.Equal(2, SchemaMigrator.MigrateToLatest(connection, earlier));

            using var insert = connection.CreateCommand();
            insert.CommandText =
                @"INSERT INTO plays (
                      SchemaVersion, UserId, ItemId, ItemType, ItemName,
                      StartedUtcTicks, EndedUtcTicks, WatchedDurationTicks, ReachedTheEnd,
                      ClientName, DeviceId, DeviceName, PlayMethod,
                      TranscodeVideoWasDirect, TranscodeAudioWasDirect, TranscodeReasons
                  ) VALUES (
                      2, $userId, $itemId, 'Movie', 'An item',
                      $started, $ended, 0, 0,
                      'Web', 'a-device', 'A device', 0,
                      1, 1, ''
                  )";
            insert.Parameters.AddWithValue("$userId", Alice.ToString("N"));
            insert.Parameters.AddWithValue("$itemId", Guid.Parse("11111111-2222-3333-4444-555555555555").ToString("N"));
            insert.Parameters.AddWithValue("$started", March.Ticks);
            insert.Parameters.AddWithValue("$ended", March.AddMinutes(10).Ticks);
            insert.ExecuteNonQuery();
        }

        using var after = new SqlitePlayStore(_root);

        Assert.Single(after.AllPlays());
        Assert.Empty(after.OpenPlays());

        after.NoteOpenPlay(ARunningPlay("play-1", Alice));

        Assert.Single(after.OpenPlays());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static OpenPlay ARunningPlay(
        string key,
        Guid userId,
        DateTime? startedUtc = null,
        TimeSpan? watched = null)
    {
        var started = startedUtc ?? March;

        return new OpenPlay
        {
            PlayKey = key,
            SoFar = AFinishedPlay(userId) with
            {
                StartedUtc = started,
                EndedUtc = started + (watched ?? TimeSpan.Zero),
                WatchedDuration = watched ?? TimeSpan.Zero,
                ReachedTheEnd = false
            }
        };
    }

    private static PlayRecord AFinishedPlay(Guid userId)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = March,
            EndedUtc = March.AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.DirectPlay,
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
