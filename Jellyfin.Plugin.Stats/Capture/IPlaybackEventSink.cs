using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Where the subscription hands an event on to. This is the seam between the
/// part that listens and the part that writes.
/// </summary>
/// <remarks>
/// The listener holds no state and makes no decision about a play. Everything
/// that decides what a play is, and everything that reaches a store, lives
/// behind this interface, so the write path can be built, replaced or made
/// asynchronous without the subscription being touched. The interface names no
/// storage technology for the same reason.
/// <para>
/// A member here returns void rather than a task on purpose. These are called
/// from the server's own event dispatch, on the thread that raised the event,
/// and a task nobody awaits is an exception nobody sees. A sink that needs to
/// do slow work queues it and returns.
/// </para>
/// </remarks>
public interface IPlaybackEventSink
{
    /// <summary>
    /// A session started playing an item.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    void PlaybackStarted(PlaybackProgressEventArgs args);

    /// <summary>
    /// A session reported how far into an item it is.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    void PlaybackProgressed(PlaybackProgressEventArgs args);

    /// <summary>
    /// A session stopped playing an item.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    void PlaybackStopped(PlaybackStopEventArgs args);

    /// <summary>
    /// A session ended. A play that was still open on that session ended
    /// without a stop event, which is what this tells the write path.
    /// </summary>
    /// <param name="args">The event the server raised.</param>
    void SessionEnded(SessionEventArgs args);
}
