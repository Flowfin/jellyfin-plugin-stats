// What a whole play leaves on the server log, read the way an administrator
// reads it: the formatted lines, not the format strings.
//
// The names are deliberate nonsense words. A real name like "viewer" or "An
// Item" occurs inside other words and inside the plugin's own vocabulary, so an
// assertion that it is absent could pass or fail for reasons that have nothing
// to do with what was logged.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class WhatTheLogContainsTests
{
    private const string TheUsersName = "quernbeck";
    private const string TheItemsTitle = "The Kettleford Inheritance";
    private const string ThePlaySessionId = "play-1";
    private const string TheSessionId = "session-1";

    /// <summary>
    /// The only path in the plugin that writes a log line at all is a handler
    /// that faulted, so that is the path this drives. A play through a sink that
    /// works writes nothing, which is the case below, and an assertion over an
    /// empty log proves nothing about what a line would have carried.
    /// </summary>
    [Fact]
    public async Task AWholePlayThatFailsNamesNobodyOnTheLog()
    {
        var sessions = new FakeSessionManager();
        var logger = new RecordingLogger<PlaybackEventListener>();
        var listener = new PlaybackEventListener(sessions, new FaultingSink(), logger);

        await listener.StartAsync(CancellationToken.None);
        RaiseAWholePlay(sessions);
        await listener.StopAsync(CancellationToken.None);

        // One line per event, so the assertions below are about four lines and
        // not about an empty list.
        Assert.Equal(4, logger.Lines.Count);

        Assert.All(logger.Lines, line =>
        {
            Assert.DoesNotContain(TheUsersName, line.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TheItemsTitle, line.Message, StringComparison.OrdinalIgnoreCase);
        });

        // An identifier is on every line, which is what makes the two
        // assertions above mean something: a message that had dropped every
        // value would pass them and fail these. Three lines carry the play and
        // one carries the session, because a session that has ended has no play
        // left to name.
        Assert.Equal(3, logger.Lines.Count(line => line.Message.Contains(ThePlaySessionId, StringComparison.Ordinal)));
        Assert.Single(logger.Lines, line => line.Message.Contains(TheSessionId, StringComparison.Ordinal));
    }

    /// <summary>
    /// A play that works writes nothing at all. There is no line at information
    /// level about a play having been recorded, because a line per play on a
    /// server with a household on it is a record of what that household watched,
    /// written into a file none of this plan's retention rules reach.
    /// </summary>
    [Fact]
    public async Task AWholePlayThatWorksWritesNothingAtAll()
    {
        var sessions = new FakeSessionManager();
        var logger = new RecordingLogger<PlaybackEventListener>();
        var listener = new PlaybackEventListener(sessions, new KeepsNothingSink(), logger);

        await listener.StartAsync(CancellationToken.None);
        RaiseAWholePlay(sessions);
        await listener.StopAsync(CancellationToken.None);

        Assert.Empty(logger.Lines);
    }

    /// <summary>
    /// One play from start to stop, on a session carrying a user name and an
    /// item title the assertions can look for.
    /// </summary>
    /// <param name="sessions">The session manager to raise the play on.</param>
    private static void RaiseAWholePlay(FakeSessionManager sessions)
    {
        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser(TheUsersName))
            .Playing(PlaySessionBuilder.Video(TheItemsTitle, TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Identified(TheSessionId, ThePlaySessionId)
            .Via(PlayMethod.DirectPlay)
            .Build();

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10));
        sessions.RaiseSessionEnded(session);
    }

    /// <summary>
    /// A sink that faults on every event, so the guarded path that logs is the
    /// one taken.
    /// </summary>
    private sealed class FaultingSink : IPlaybackEventSink
    {
        public void PlaybackStarted(PlaybackProgressEventArgs args) => throw new InvalidOperationException("start");

        public void PlaybackProgressed(PlaybackProgressEventArgs args) => throw new InvalidOperationException("progress");

        public void PlaybackStopped(PlaybackStopEventArgs args) => throw new InvalidOperationException("stop");

        public void SessionEnded(SessionEventArgs args) => throw new InvalidOperationException("session end");
    }
}
