using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// The sink that is registered until there is a store to write to. It reads
/// each event and keeps nothing.
/// </summary>
/// <remarks>
/// It exists so the subscription can be built, registered and proved before the
/// write path is. The name says what it does: a reader who finds this resolved
/// in a running server is looking at a plugin that is capturing nothing, and
/// that is a fact worth being able to see rather than a default worth hiding.
/// Issue #29 replaces the registration in
/// <see cref="PluginServiceRegistrator"/>, and nothing else changes.
/// </remarks>
public sealed class DiscardingPlaybackEventSink : IPlaybackEventSink
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
