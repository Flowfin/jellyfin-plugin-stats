// The store, driven over a temporary directory. Nothing here needs a server, a
// socket or a service: the store takes the folder it writes into as an
// argument, so a test hands it one it made and deletes it afterwards.
//
// Two rows carry the whole shape between them. One fills every field that can
// be absent and one leaves every one of them absent, because a column that is
// only ever written full is a column whose null half nothing has read.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class PlayStoreTests : IDisposable
{
    private readonly string _root;

    public PlayStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// A clean data folder is the state of every first start. The folder itself
    /// does not exist yet here, which is one step further back than an empty
    /// one, because the server creates the folder lazily too.
    /// </summary>
    [Fact]
    public void TheStoreIsCreatedOnFirstUseInACleanDataFolder()
    {
        Assert.False(Directory.Exists(_root));

        using (var store = new SqlitePlayStore(_root))
        {
            Assert.Empty(store.MostRecentPlays(10));
        }

        Assert.True(File.Exists(Path.Combine(_root, SqlitePlayStore.FileName)));
    }

    /// <summary>
    /// Opening a store that is already there keeps what is in it. This is the
    /// second start and every start after it, and it is the case a create that
    /// is not conditional would destroy.
    /// </summary>
    [Fact]
    public void AStoreThatIsAlreadyThereIsOpenedRatherThanReplaced()
    {
        using (var first = new SqlitePlayStore(_root))
        {
            first.Add(APlay());
        }

        using var second = new SqlitePlayStore(_root);

        Assert.Single(second.MostRecentPlays(10));
    }

    /// <summary>
    /// Every field of the row shape survives the round trip, with the absent
    /// half of every optional field absent rather than defaulted.
    /// </summary>
    [Fact]
    public void EveryFieldOfAPlayComesBackAsItWentIn()
    {
        var play = APlay();

        using var store = new SqlitePlayStore(_root);
        store.Add(play);

        var read = Assert.Single(store.MostRecentPlays(10));
        AssertRoundTripped(play, read);
    }

    [Fact]
    public void APlayWithNothingOptionalOnItComesBackWithThoseFieldsStillAbsent()
    {
        var play = APlay() with
        {
            ParentId = null,
            ItemRuntime = null,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };

        using var store = new SqlitePlayStore(_root);
        store.Add(play);

        var read = Assert.Single(store.MostRecentPlays(10));
        Assert.Null(read.ParentId);
        Assert.Null(read.ItemRuntime);
        Assert.Null(read.Transcode.VideoCodec);
        Assert.Null(read.Transcode.PeakBitrate);
        Assert.Null(read.Transcode.HardwareAcceleration);

        // Empty rather than a list holding one empty string, which is what a
        // split of an empty column produces if nothing stands in front of it.
        Assert.Empty(read.Transcode.Reasons);
        AssertRoundTripped(play, read);
    }

    /// <summary>
    /// The read is bounded by its argument and by nothing else, and it hands
    /// back the newest plays rather than the first ones it happens to meet.
    /// </summary>
    [Fact]
    public void TheReadReturnsNoMoreRowsThanItWasAskedFor()
    {
        var start = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

        using var store = new SqlitePlayStore(_root);
        for (var i = 0; i < 5; i++)
        {
            store.Add(APlay() with
            {
                ItemName = i.ToString(CultureInfo.InvariantCulture),
                StartedUtc = start.AddHours(i),
                EndedUtc = start.AddHours(i).AddMinutes(30)
            });
        }

        var read = store.MostRecentPlays(2);

        Assert.Equal(2, read.Count);
        Assert.Equal(new[] { "4", "3" }, read.Select(play => play.ItemName));
    }

    [Fact]
    public void AReadForNoRowsAtAllIsRefusedRatherThanAnsweredWithNothing()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.MostRecentPlays(0));
    }

    /// <summary>
    /// A timestamp that is not in UTC would be stored as if it were and read
    /// back as a different moment, off by the writer's offset. Both timestamps
    /// on the row are driven, because a guard on one of them is not a guard on
    /// the other and the two are separate arguments.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ATimestampThatIsNotUtcIsRefused(DateTimeKind kind)
    {
        var moment = DateTime.SpecifyKind(new DateTime(2026, 3, 14, 9, 0, 0), kind);

        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentException>(() => store.Add(APlay() with { StartedUtc = moment }));
        Assert.Throws<ArgumentException>(() => store.Add(APlay() with { EndedUtc = moment }));
        Assert.Empty(store.MostRecentPlays(10));
    }

    /// <summary>
    /// The reasons share one column, so a reason carrying the character that
    /// separates them would come back as two. It is refused at the write, which
    /// is the last moment the original is still there.
    /// </summary>
    [Fact]
    public void ATranscodeReasonThatWouldSplitInTwoIsRefused()
    {
        var play = APlay() with
        {
            Transcode = APlay().Transcode with { Reasons = ["VideoCodecNotSupported|AudioBitrateNotSupported"] }
        };

        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentException>(() => store.Add(play));
        Assert.Empty(store.MostRecentPlays(10));
    }

    /// <summary>
    /// A store nobody has recorded into answers with nothing rather than with a
    /// date. This is what the configuration page meets on a first start, and a
    /// sentinel here would be drawn as a real moment in the first year a clock
    /// can name.
    /// </summary>
    [Fact]
    public void AStoreWithNoRowsHasNoOldestPlay()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Null(store.OldestPlayStartedUtc());
    }

    /// <summary>
    /// The oldest play is the earliest one started, and it comes back in UTC.
    /// </summary>
    [Fact]
    public void TheOldestPlayIsTheEarliestOneStarted()
    {
        var earliest = new DateTime(2025, 6, 1, 7, 30, 0, DateTimeKind.Utc);

        using var store = new SqlitePlayStore(_root);
        store.Add(APlay() with { StartedUtc = earliest.AddDays(9), EndedUtc = earliest.AddDays(9).AddHours(1) });
        store.Add(APlay() with { StartedUtc = earliest, EndedUtc = earliest.AddHours(1) });
        store.Add(APlay() with { StartedUtc = earliest.AddDays(4), EndedUtc = earliest.AddDays(4).AddHours(1) });

        var oldest = store.OldestPlayStartedUtc();

        Assert.Equal(earliest, oldest);
        Assert.Equal(DateTimeKind.Utc, oldest!.Value.Kind);
    }

    /// <summary>
    /// The row written last can be the oldest play. An import reads a file in
    /// whatever order that file holds, so the newest row in the table is not
    /// the newest play in it, and a store answering by write order would tell
    /// an administrator who has just imported a year that it knows nothing
    /// older than this afternoon.
    /// </summary>
    [Fact]
    public void TheOldestPlayIsNotTheFirstRowWritten()
    {
        var earliest = new DateTime(2024, 2, 29, 21, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 5, 5, 5, 0, 0, DateTimeKind.Utc);

        using var store = new SqlitePlayStore(_root);
        store.Add(APlay() with { StartedUtc = latest, EndedUtc = latest.AddMinutes(30) });
        store.Add(APlay() with { StartedUtc = earliest, EndedUtc = earliest.AddMinutes(30) });

        // The row that went in first is the later play, and AllPlays walks in
        // write order, so this says the two orders really do disagree here
        // rather than leaving that to the reader.
        Assert.Equal(latest, store.AllPlays().First().StartedUtc);
        Assert.Equal(earliest, store.OldestPlayStartedUtc());
    }

    /// <summary>
    /// The answer moves when the sweep takes the rows behind it. The figure is
    /// how far back the plugin can answer for, so a retention window that has
    /// just deleted a year has to show up here; a value read once and kept
    /// would go on claiming rows that are gone from the file.
    /// </summary>
    [Fact]
    public void TheOldestPlayFollowsTheRetentionSweep()
    {
        var earliest = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cutoff = earliest.AddDays(30);
        var survivor = earliest.AddDays(45);

        using var store = new SqlitePlayStore(_root);
        store.Add(APlay() with { StartedUtc = earliest, EndedUtc = earliest.AddMinutes(20) });
        store.Add(APlay() with { StartedUtc = survivor, EndedUtc = survivor.AddMinutes(20) });

        Assert.Equal(earliest, store.OldestPlayStartedUtc());

        Assert.Equal(1, store.DeletePlaysStartedBefore(cutoff, 100));

        Assert.Equal(survivor, store.OldestPlayStartedUtc());
    }

    /// <summary>
    /// A sweep that took everything leaves the same nothing a first start does.
    /// The empty case is reached twice over the life of a server and only one
    /// of them is the one somebody writes a test for.
    /// </summary>
    [Fact]
    public void AStoreTheSweepEmptiedHasNoOldestPlayAgain()
    {
        var started = new DateTime(2025, 8, 20, 12, 0, 0, DateTimeKind.Utc);

        using var store = new SqlitePlayStore(_root);
        store.Add(APlay() with { StartedUtc = started, EndedUtc = started.AddMinutes(20) });

        Assert.Equal(1, store.DeletePlaysStartedBefore(started.AddDays(1), 100));

        Assert.Null(store.OldestPlayStartedUtc());
    }

    [Fact]
    public void TheStoreRefusesToBeBuiltOnNothing()
    {
        Assert.Throws<ArgumentNullException>(() => new SqlitePlayStore(null!));
    }

    [Fact]
    public void TheStoreRefusesToWriteNothing()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentNullException>(() => store.Add(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Compares a row that went in with the row that came back.
    /// </summary>
    /// <remarks>
    /// Record equality compares the reason list by reference, so a row that
    /// round tripped perfectly still fails a plain comparison. The list is
    /// compared as a sequence first and the reference is then made equal, so
    /// what the record comparison after it covers is every other field at once,
    /// including any field added to the shape later.
    /// </remarks>
    /// <param name="expected">The row that was written.</param>
    /// <param name="actual">The row that was read back.</param>
    private static void AssertRoundTripped(PlayRecord expected, PlayRecord actual)
    {
        Assert.Equal(expected.Transcode.Reasons, actual.Transcode.Reasons);

        var comparable = expected with
        {
            Transcode = expected.Transcode with { Reasons = actual.Transcode.Reasons }
        };

        Assert.Equal(comparable, actual);
    }

    /// <summary>
    /// One play with every optional field filled. Tests that want a field
    /// absent say so with a <c>with</c> expression, so what a case is about is
    /// the line that differs rather than a second literal to compare by eye.
    /// </summary>
    /// <returns>A play.</returns>
    private static PlayRecord APlay()
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.Transcode,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = true,
                PeakBitrate = 8_000_000,
                TypicalBitrate = 6_000_000,
                HardwareAcceleration = "qsv",
                Reasons = ["VideoCodecNotSupported", "AudioBitrateNotSupported"]
            }
        };
    }
}
