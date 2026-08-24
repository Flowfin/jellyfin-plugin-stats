// What the capture switch and the exclusion lists owe: they stop the writing
// and not the reporting.
//
// The two conditions the issue states are asserted by counting rows in a store,
// through the whole path a server drives - the subscription, the tracker, the
// gate and the queue. A test that read a report instead would pass on a plugin
// that had recorded the play and then hidden it, which is the failure the
// issue exists against and not one it would notice.

using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class CaptureGateTests
{
    private static readonly Guid Alice = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
    private static readonly Guid Bob = Guid.Parse("bbbbbbbb-1111-2222-3333-444444444444");

    [Fact]
    public async Task WithCaptureOffAWholePlayLeavesTheStoreEmpty()
    {
        var configuration = new PluginConfiguration { CaptureEnabled = false };
        var store = new HoldablePlayStore();

        await APlayThrough(store, configuration, Alice);

        // Counted in the store rather than read out of a report. A row that is
        // on disk and not shown is still a row on disk.
        Assert.Empty(store.Rows);
    }

    [Fact]
    public async Task ExcludingAUserStopsTheirRowsAndLeavesTheOthersAlone()
    {
        var configuration = new PluginConfiguration
        {
            CaptureEnabled = true,
            ExcludedUserIds = [Alice.ToString()]
        };

        var store = new HoldablePlayStore();

        await APlayThrough(store, configuration, Alice);
        await APlayThrough(store, configuration, Bob);

        // Both halves in one test, because an exclusion that stopped everybody
        // would pass the first half on its own and is the likelier mistake.
        Assert.Single(store.Rows);
        Assert.Equal(Bob, store.Rows[0].UserId);
    }

    [Fact]
    public async Task ExcludingAnItemTypeStopsThoseRowsAndLeavesTheOthersAlone()
    {
        var excluded = new HoldablePlayStore();
        await APlayThrough(excluded, new PluginConfiguration
        {
            CaptureEnabled = true,
            ExcludedItemTypes = ["Movie"]
        }, Alice);

        var kept = new HoldablePlayStore();
        await APlayThrough(kept, new PluginConfiguration
        {
            CaptureEnabled = true,
            ExcludedItemTypes = ["Audio"]
        }, Alice);

        // The play is a movie both times. Excluding the type it is stops it,
        // and excluding a type it is not leaves it alone; without the second
        // half a gate that refused everything would pass the first.
        Assert.Empty(excluded.Rows);
        Assert.Single(kept.Rows);
        Assert.Equal("Movie", kept.Rows[0].ItemType);
    }

    [Fact]
    public void TheSettingIsReadAtTheEventAndNotHeldFromBefore()
    {
        var configuration = new PluginConfiguration { CaptureEnabled = true };
        var recorded = new RecordingPlaySink();
        var gate = new CaptureGate(recorded, () => configuration);

        gate.Add(APlay(Alice), "a-play");

        // Changed after the gate was built, which is what an administrator on
        // the settings page does. The next play is judged against it, and no
        // restart happens in between.
        configuration.CaptureEnabled = false;
        gate.Add(APlay(Alice), "a-play");

        Assert.Single(recorded.Rows);
    }

    /// <summary>
    /// A play the settings refuse is refused while it is running as well. A
    /// gate that only judged the stop would put an excluded account's viewing
    /// on disk for the length of every play they watched and take it away
    /// afterwards, which is not what not recording means.
    /// </summary>
    [Fact]
    public void ARunningPlayIsJudgedByTheSameThreeAnswers()
    {
        var recorded = new RecordingPlaySink();
        var gate = new CaptureGate(recorded, () => new PluginConfiguration
        {
            CaptureEnabled = true,
            ExcludedUserIds = [Alice.ToString()]
        });

        gate.NoteOpen(ARunningPlay(Alice));
        gate.NoteOpen(ARunningPlay(Bob));

        // Both halves, because a gate that refused every running play would
        // pass the first on its own and is the likelier mistake.
        var kept = Assert.Single(recorded.Open);

        Assert.Equal(Bob, kept.SoFar.UserId);
    }

    /// <summary>
    /// Capture turned off part of the way through a play takes away the row
    /// that play already has. The change has to reach what is on the file
    /// because of it, and not only what would have been written after it.
    /// </summary>
    [Fact]
    public void TurningCaptureOffDuringAPlayTakesAwayTheRowItAlreadyHas()
    {
        var configuration = new PluginConfiguration { CaptureEnabled = true };
        var recorded = new RecordingPlaySink();
        var gate = new CaptureGate(recorded, () => configuration);

        gate.NoteOpen(ARunningPlay(Alice));

        Assert.Single(recorded.Open);
        Assert.Empty(recorded.Forgotten);

        configuration.CaptureEnabled = false;
        gate.Add(APlay(Alice), "a-play");

        Assert.Empty(recorded.Rows);
        Assert.Equal("a-play", Assert.Single(recorded.Forgotten));
    }

    /// <summary>
    /// Taking a running row away is passed through without being judged. A
    /// removal a setting could refuse is a removal that leaves something behind
    /// exactly when the setting says not to keep it.
    /// </summary>
    [Fact]
    public void TakingARunningRowAwayIsNeverRefused()
    {
        var recorded = new RecordingPlaySink();
        var gate = new CaptureGate(recorded, () => new PluginConfiguration { CaptureEnabled = false });

        gate.ForgetOpen("a-play");

        Assert.Equal("a-play", Assert.Single(recorded.Forgotten));
    }

    /// <summary>
    /// The gate refuses to judge a running play it was not given, rather than
    /// reading a missing one as one nobody excluded.
    /// </summary>
    [Fact]
    public void TheGateRefusesARunningPlayItWasNotGiven()
    {
        var gate = new CaptureGate(new RecordingPlaySink(), () => new PluginConfiguration());

        Assert.Throws<ArgumentNullException>(() => gate.NoteOpen(null!));
    }

    [Fact]
    public void AUserIdentifierIsMatchedAsAnIdentifierAndNotAsText()
    {
        var configuration = new PluginConfiguration
        {
            CaptureEnabled = true,

            // The same user as Alice, in braces and upper case. The setter
            // accepts it, because it is an identifier; compared as text it is
            // a different string and the exclusion would silently not apply.
            ExcludedUserIds = [Alice.ToString("B").ToUpperInvariant()]
        };

        Assert.False(CaptureGate.Records(APlay(Alice), configuration));
        Assert.True(CaptureGate.Records(APlay(Bob), configuration));
    }

    [Fact]
    public void AnItemTypeTheServerDoesNotKnowNeverReachesTheDecision()
    {
        var configuration = new PluginConfiguration
        {
            CaptureEnabled = true,

            // Lower case, which is not how the server spells the kind. The
            // configuration refuses it and keeps its default, so the exclusion
            // the administrator thought they had made is not one, and the page
            // says so through RejectedFields rather than the play being
            // silently kept or silently dropped here.
            ExcludedItemTypes = ["movie"]
        };

        Assert.Empty(configuration.ExcludedItemTypes);
        Assert.Contains(nameof(PluginConfiguration.ExcludedItemTypes), configuration.RejectedFields);
        Assert.True(CaptureGate.Records(APlay(Alice), configuration));
    }

    [Fact]
    public void NothingIsRefusedWhenNothingIsExcluded()
    {
        var configuration = new PluginConfiguration { CaptureEnabled = true };

        Assert.True(CaptureGate.Records(APlay(Alice), configuration));
        Assert.True(CaptureGate.Records(APlay(Bob), configuration));
    }

    [Fact]
    public void TheDecisionRefusesToBeMadeAboutNothing()
    {
        var configuration = new PluginConfiguration();

        Assert.Throws<ArgumentNullException>(() => CaptureGate.Records(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => CaptureGate.Records(APlay(Alice), null!));
    }

    /// <summary>
    /// Runs one whole play for a user through the path a server drives, and
    /// waits until the write path has finished with it.
    /// </summary>
    private static OpenPlay ARunningPlay(Guid userId)
        => new() { PlayKey = "a-play", SoFar = APlay(userId) };

    private static async Task APlayThrough(IPlayStore store, PluginConfiguration configuration, Guid userId)
    {
        using var writer = new QueuedPlayWriter(() => store, QueuedPlayWriter.DefaultBound, NullLogger<QueuedPlayWriter>.Instance);
        var gate = new CaptureGate(writer, () => configuration);
        var tracker = new PlayTracker(gate, NullLogger<PlayTracker>.Instance);
        var sessions = new FakeSessionManager();
        var listener = new PlaybackEventListener(sessions, tracker, NothingWasLeftOpen.Pass(), NullLogger<PlaybackEventListener>.Instance);

        await listener.StartAsync(CancellationToken.None);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer", userId))
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(ServerPlayMethod.DirectPlay)
            .Build();

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10));

        await listener.StopAsync(CancellationToken.None);

        // Draining is part of the assertion rather than a tidy-up. Without it
        // an empty store means the row has not arrived yet, which is what a
        // test claiming nothing was recorded must not be able to mean.
        writer.Dispose();
    }

    private static PlayRecord APlay(Guid userId)
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
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
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
    }

    private sealed class RecordingPlaySink : IPlaySink
    {
        private readonly System.Collections.Generic.List<PlayRecord> _rows = new();
        private readonly System.Collections.Generic.List<OpenPlay> _open = new();
        private readonly System.Collections.Generic.List<string> _forgotten = new();

        public System.Collections.Generic.IReadOnlyList<PlayRecord> Rows => _rows;

        /// <summary>
        /// Gets the running plays this was handed.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<OpenPlay> Open => _open;

        /// <summary>
        /// Gets the keys this was told to take away without a finished play.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> Forgotten => _forgotten;

        public void Add(PlayRecord play, string playKey) => _rows.Add(play);

        public void NoteOpen(OpenPlay play) => _open.Add(play);

        public void ForgetOpen(string playKey) => _forgotten.Add(playKey);
    }
}
