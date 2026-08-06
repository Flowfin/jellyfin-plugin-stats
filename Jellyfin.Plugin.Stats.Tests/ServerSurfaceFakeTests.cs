// What the fakes are for, stated as tests rather than as a claim in a comment.
// Each one is a thing the capture milestone will need to do and cannot do
// without a server otherwise.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// Tests over the fakes that stand in for the server surfaces this plugin
/// reads.
/// </summary>
public class ServerSurfaceFakeTests
{
    [Fact]
    public void A_whole_play_can_be_produced_in_one_method_with_no_server()
    {
        var user = FakeUserManager.NewUser("ada");
        var sessions = new FakeSessionManager();
        var session = new PlaySessionBuilder(sessions)
            .ForUser(user)
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(PlayMethod.DirectPlay)
            .Build();

        var seen = new List<string>();
        sessions.PlaybackStart += (_, args) => seen.Add("start@" + args.PlaybackPositionTicks);
        sessions.PlaybackProgress += (_, args) => seen.Add("progress@" + args.PlaybackPositionTicks);
        sessions.PlaybackStopped += (_, args) => seen.Add("stop@" + args.PlaybackPositionTicks);

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(10));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(90), playedToCompletion: true);

        Assert.Equal(
            new[]
            {
                "start@0",
                "progress@" + TimeSpan.FromMinutes(10).Ticks,
                "stop@" + TimeSpan.FromMinutes(90).Ticks
            },
            seen);
    }

    [Fact]
    public void The_events_carry_the_user_the_item_the_client_and_the_device()
    {
        var user = FakeUserManager.NewUser("ada");
        var item = PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90));
        var sessions = new FakeSessionManager();
        var session = new PlaySessionBuilder(sessions)
            .ForUser(user)
            .Playing(item)
            .From("Jellyfin Android", "A phone", "device-7")
            .Build();

        PlaybackProgressEventArgs? started = null;
        sessions.PlaybackStart += (_, args) => started = args;

        sessions.RaisePlaybackStart(session);

        Assert.NotNull(started);
        Assert.Equal(user.Id, Assert.Single(started!.Users).Id);
        Assert.Same(item, started.Item);
        Assert.Equal("Jellyfin Android", started.ClientName);
        Assert.Equal("A phone", started.DeviceName);
        Assert.Equal("device-7", started.DeviceId);
        Assert.Same(session, started.Session);
    }

    [Fact]
    public void A_transcoded_play_carries_its_codecs_acceleration_and_reasons()
    {
        var user = FakeUserManager.NewUser("ada");
        var sessions = new FakeSessionManager();
        var session = new PlaySessionBuilder(sessions)
            .ForUser(user)
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .Transcoding(
                videoCodec: "h264",
                audioCodec: "aac",
                hardwareAcceleration: HardwareAccelerationType.qsv,
                reasons: TranscodeReason.VideoCodecNotSupported | TranscodeReason.ContainerBitrateExceedsLimit)
            .Build();

        PlaybackProgressEventArgs? started = null;
        sessions.PlaybackStart += (_, args) => started = args;

        sessions.RaisePlaybackStart(session);

        Assert.NotNull(started);
        var transcoding = started!.Session.TranscodingInfo;
        Assert.NotNull(transcoding);
        Assert.Equal("h264", transcoding!.VideoCodec);
        Assert.Equal("aac", transcoding.AudioCodec);
        Assert.Equal(HardwareAccelerationType.qsv, transcoding.HardwareAccelerationType);
        Assert.Equal(
            TranscodeReason.VideoCodecNotSupported | TranscodeReason.ContainerBitrateExceedsLimit,
            transcoding.TranscodeReasons);
        Assert.Equal(PlayMethod.Transcode, started.Session.PlayState.PlayMethod);
    }

    [Fact]
    public void A_paused_progress_report_says_so()
    {
        var user = FakeUserManager.NewUser("ada");
        var sessions = new FakeSessionManager();
        var session = new PlaySessionBuilder(sessions)
            .ForUser(user)
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .Build();

        var paused = new List<bool>();
        sessions.PlaybackProgress += (_, args) => paused.Add(args.IsPaused);

        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(1));
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(2), isPaused: true);

        Assert.Equal(new[] { false, true }, paused);
    }

    [Fact]
    public void The_user_manager_answers_the_lookups_an_event_needs()
    {
        var ada = FakeUserManager.NewUser("ada");
        var grace = FakeUserManager.NewUser("grace");
        var users = new FakeUserManager(ada, grace);

        Assert.Equal(ada.Id, users.GetUserById(ada.Id)!.Id);
        Assert.Equal(grace.Id, users.GetUserByName("GRACE")!.Id);
        Assert.Equal(ada.Id, users.GetFirstUser()!.Id);
        Assert.Equal(new[] { ada.Id, grace.Id }, users.GetUsersIds());

        Assert.True(users.Forget(ada.Id));
        Assert.Null(users.GetUserById(ada.Id));
    }

    [Fact]
    public void A_member_outside_the_surface_refuses_rather_than_answering()
    {
        var sessions = new FakeSessionManager();
        var users = new FakeUserManager();

        // A default return here would let a test agree with a value no server
        // ever produced, which is the failure this shape exists against.
        Assert.Throws<NotSupportedException>(() => sessions.ClearTranscodingInfo("device-1"));
        Assert.Throws<NotSupportedException>(() => users.GetAuthenticationProviders());
    }

    [Fact]
    public void A_session_the_manager_was_never_told_about_refuses()
    {
        var user = FakeUserManager.NewUser("ada");
        var known = new FakeSessionManager();
        var session = new PlaySessionBuilder(known)
            .ForUser(user)
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .Build();

        var other = new FakeSessionManager();

        Assert.Throws<InvalidOperationException>(() => other.RaisePlaybackStart(session));
    }
}
