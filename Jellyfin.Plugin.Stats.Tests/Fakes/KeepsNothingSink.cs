// A sink that reads every event and keeps nothing, for the tests whose subject
// is the subscription rather than what is written. It lives in the suite and
// not in the plugin: the plugin registers the real write path, and a
// keeps-nothing sink in the shipped assembly would be a type a reader has to
// check the registrations to rule out.

using Jellyfin.Plugin.Stats.Capture;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// An <see cref="IPlaybackEventSink"/> that does nothing with what it is given.
/// </summary>
public sealed class KeepsNothingSink : IPlaybackEventSink
{
    /// <inheritdoc />
    public void PlaybackStarted(PlaybackProgressEventArgs args)
    {
    }

    /// <inheritdoc />
    public void PlaybackProgressed(PlaybackProgressEventArgs args)
    {
    }

    /// <inheritdoc />
    public void PlaybackStopped(PlaybackStopEventArgs args)
    {
    }

    /// <inheritdoc />
    public void SessionEnded(SessionEventArgs args)
    {
    }
}
