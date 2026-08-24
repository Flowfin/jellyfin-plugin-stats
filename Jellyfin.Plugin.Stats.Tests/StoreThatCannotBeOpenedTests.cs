// What a server sees when this plugin cannot open its store at all.
//
// The neighbouring file, StoreFromALaterBuildTests, drives the one refusal
// where the file is perfectly readable and the plugin is out of date. This one
// drives the two the first two conditions of issue #31 name: a file that is not
// a database, and a folder that cannot be made. Both go through a real
// SqlitePlayStore over a real path rather than through a function that throws,
// because the thing being asserted is what the store does with the file and not
// what a stand-in was told to do.
//
// The headless policy refuses running a server to watch it stay up, so what is
// asserted instead is the property a server depends on: the failure is met on
// the writer's own thread, counted, reported once, and said out loud, and
// nothing is thrown back at whoever handed the play over.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class StoreThatCannotBeOpenedTests : IDisposable
{
    private readonly string _root;

    public StoreThatCannotBeOpenedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// The first condition of issue #31. A file sitting where the store belongs
    /// that is not a database at all: the rows are lost and counted, one line
    /// says why, the plugin says it cannot store anything, and neither the call
    /// that handed the play over nor the shutdown throws.
    /// </summary>
    /// <remarks>
    /// The bytes are written rather than a failure being simulated, so what
    /// refuses here is SQLite reading a header it does not recognise. A store
    /// that opened and then refused a statement would be a different failure
    /// and is the case below it.
    /// </remarks>
    [Fact]
    public void AFileThatIsNotADatabaseLeavesTheServerRunningAndThePluginSayingSo()
    {
        AFileWhereTheStoreBelongs("this is not a database, it is a text file");

        var logger = new RecordingLogger<QueuedPlayWriter>();
        using var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(_root),
            QueuedPlayWriter.DefaultBound,
            logger);

        // Neither of these may throw. Add is reached from inside the server's
        // own event dispatch and Dispose from its shutdown.
        writer.Add(APlay(), "a-play");
        writer.Add(APlay(), "a-play");
        writer.Dispose();

        Assert.Equal(2, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.Equal(typeof(SqliteException).FullName, writer.WhyTheStoreCouldNotBeOpened);
        Assert.Single(logger.Lines);
    }

    /// <summary>
    /// The second condition of issue #31, as close to it as a test may get. A
    /// read-only volume is a change to the machine the suite runs on and the
    /// headless policy refuses making one, so what is put in the way instead is
    /// a file sitting where the data folder has to be, which the operating
    /// system refuses to turn into a directory. The substitution is stated
    /// rather than passed off as the real thing: what this proves is the
    /// plugin's behaviour when the folder cannot be had, and not that a
    /// read-only mount produces exactly this exception.
    /// </summary>
    [Fact]
    public void AFolderThatCannotBeMadeLeavesTheSameStatement()
    {
        var wherever = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(wherever, "a file is standing where the data folder has to go");

        var logger = new RecordingLogger<QueuedPlayWriter>();
        using var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(wherever),
            QueuedPlayWriter.DefaultBound,
            logger);

        writer.Add(APlay(), "a-play");
        writer.Dispose();

        Assert.Equal(1, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.NotNull(writer.WhyTheStoreCouldNotBeOpened);
        Assert.Single(logger.Lines);
    }

    /// <summary>
    /// A store that opens says nothing. The statement is about the store and
    /// not about how much has been written, so a plugin on a quiet server is
    /// not a plugin reporting a fault.
    /// </summary>
    [Fact]
    public void AStoreThatOpensLeavesNothingToSay()
    {
        using (var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(_root),
            QueuedPlayWriter.DefaultBound,
            new RecordingLogger<QueuedPlayWriter>()))
        {
            writer.Add(APlay(), "a-play");
            writer.Dispose();

            Assert.Equal(1, writer.Written);
            Assert.Null(writer.WhyTheStoreCouldNotBeOpened);
        }
    }

    /// <summary>
    /// The statement is the last attempt rather than a verdict. A file that
    /// could not be opened and then can is a plugin that works again on the
    /// next play, without anybody restarting the server, which is what a lock
    /// somebody clears in a minute deserves.
    /// </summary>
    [Fact]
    public void AStoreThatOpensAfterAFailureStopsSayingItCannot()
    {
        AFileWhereTheStoreBelongs("this is not a database, it is a text file");

        var attempts = 0;
        using var writer = new QueuedPlayWriter(
            () =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return new SqlitePlayStore(_root);
                }

                File.Delete(Path.Combine(_root, SqlitePlayStore.FileName));

                return new SqlitePlayStore(_root);
            },
            QueuedPlayWriter.DefaultBound,
            new RecordingLogger<QueuedPlayWriter>());

        writer.Add(APlay(), "a-play");

        // Two calls to Add rather than one loop: the writer opens the store
        // when it has a row and no store, so the second attempt needs a second
        // row to arrive.
        WaitUntilTheFirstAttemptIsIn(writer);
        writer.Add(APlay(), "a-play");
        writer.Dispose();

        Assert.Equal(2, attempts);
        Assert.Equal(1, writer.Failed);
        Assert.Equal(1, writer.Written);
        Assert.Null(writer.WhyTheStoreCouldNotBeOpened);
    }

    /// <summary>
    /// A row the store refused after it opened is one row, not a broken plugin.
    /// The distinction is the one issue #31 draws between a file that cannot be
    /// opened at all and a file that opens and then refuses a statement, and it
    /// is the reason a plugin does not switch itself off over a single bad row.
    /// </summary>
    [Fact]
    public void ARowTheOpenStoreRefusedIsCountedAndSaysNothingAboutTheStore()
    {
        var store = new HoldablePlayStore
        {
            Throwing = () => new InvalidOperationException("this store takes no rows")
        };

        using var writer = new QueuedPlayWriter(
            () => store,
            QueuedPlayWriter.DefaultBound,
            new RecordingLogger<QueuedPlayWriter>());

        writer.Add(APlay(), "a-play");
        writer.Dispose();

        Assert.Equal(1, writer.Failed);
        Assert.Equal(0, writer.Written);
        Assert.Null(writer.WhyTheStoreCouldNotBeOpened);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// Blocks until the writer has met the first open, which is the moment the
    /// statement it is about has been made. Polling rather than a signal,
    /// because the thing waited on is inside the writer and the writer offers
    /// no handle on it.
    /// </summary>
    /// <param name="writer">The writer.</param>
    private static void WaitUntilTheFirstAttemptIsIn(QueuedPlayWriter writer)
    {
        for (var i = 0; i < 500 && writer.Failed == 0; i++)
        {
            System.Threading.Thread.Sleep(10);
        }

        Assert.Equal(1, writer.Failed);
    }

    private static PlayRecord APlay() => new()
    {
        SchemaVersion = SqlitePlayStore.SchemaVersion,
        UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Episode",
        ParentId = null,
        ItemName = "An episode",
        ItemRuntime = TimeSpan.FromMinutes(42),
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
            VideoCodec = "h264",
            AudioCodec = "aac",
            VideoWasDirect = true,
            AudioWasDirect = true,
            PeakBitrate = null,
            TypicalBitrate = null,
            HardwareAcceleration = null,
            Reasons = Array.Empty<string>()
        }
    };

    /// <summary>
    /// Writes bytes into the file the store would open, so that opening it
    /// fails on what is in it rather than on where it is.
    /// </summary>
    /// <param name="contents">What to put there.</param>
    private void AFileWhereTheStoreBelongs(string contents)
        => File.WriteAllText(Path.Combine(_root, SqlitePlayStore.FileName), contents);
}
