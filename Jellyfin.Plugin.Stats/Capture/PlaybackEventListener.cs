using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Subscribes to the server's session events while the host is running, and
/// hands each one to the write path.
/// </summary>
/// <remarks>
/// This is a type of its own rather than a method on the plugin class. A plugin
/// is constructed by the plugin manager to be asked its name and its pages; the
/// host's start and stop are a different lifetime, and joining them would tie
/// the capture path to a static instance that a test cannot replace. Everything
/// this type needs arrives through its constructor.
/// <para>
/// Five of the eight events the session manager declares are not subscribed to.
/// A play is known from playback start, and a play that ends badly is known
/// from session end; session activity, controller connection, capability
/// changes and session start carry nothing this plugin reads. A subscription
/// nobody needs is a handler that runs on every session in the server for no
/// reason, so the set is the smallest one the capture milestone asks for and
/// widening it is a diff.
/// </para>
/// <para>
/// It also finishes what a previous process left running, and it does that
/// BEFORE the first subscription rather than beside it. The plays on the file at
/// that instant are exactly the ones no process is tracking, because this one
/// has not written any yet, and a pass made after the subscriptions were live
/// would meet a play that is being watched right now. That ordering is the whole
/// argument, so the two statements are in one method in this order rather than
/// in two registrations whose order a reader would have to reconstruct. Issue
/// #221.
/// </para>
/// </remarks>
public sealed class PlaybackEventListener : IHostedService
{
    private readonly ISessionManager _sessionManager;
    private readonly IPlaybackEventSink _sink;
    private readonly FinishWhatARestartLeftOpen _leftOpen;
    private readonly ILogger<PlaybackEventListener> _logger;
    private bool _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackEventListener"/> class.
    /// </summary>
    /// <param name="sessionManager">The session manager whose events are read.</param>
    /// <param name="sink">Where events are handed on to.</param>
    /// <param name="leftOpen">What finishes the plays a previous process left running, before anything is subscribed to.</param>
    /// <param name="logger">The logger.</param>
    public PlaybackEventListener(
        ISessionManager sessionManager,
        IPlaybackEventSink sink,
        FinishWhatARestartLeftOpen leftOpen,
        ILogger<PlaybackEventListener> logger)
    {
        _sessionManager = sessionManager;
        _sink = sink;
        _leftOpen = leftOpen;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Subscribing twice would deliver every event twice and count every play
    /// twice, and nothing in the server promises a single start. The flag makes
    /// a second start without a stop do nothing rather than double the numbers.
    /// It also makes the pass over what a restart left open run once, which
    /// matters more than the subscriptions do: the second run would meet the
    /// plays this process had started since the first.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return Task.CompletedTask;
        }

        FinishWhatWasLeftOpen();

        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.SessionEnded += OnSessionEnded;
        _subscribed = true;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Unsubscribing is what keeps a plugin that is disabled and re-enabled from
    /// leaving a handler behind on a session manager that outlives it. The same
    /// flag keeps a stop without a start from removing handlers this instance
    /// never added.
    /// </remarks>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            return Task.CompletedTask;
        }

        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.SessionEnded -= OnSessionEnded;
        _subscribed = false;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the pass over what a previous process left open, and swallows
    /// whatever it throws.
    /// </summary>
    /// <remarks>
    /// The pass opens the store, and an open that fails is this plugin's failure
    /// and nobody else's. Letting it out of here would travel into the host's
    /// own start, which is the one place issue #31 says a store must never
    /// reach. The plays stay on the file, a later start tries again, and the
    /// events below are subscribed to either way.
    /// <para>
    /// The exception's type is written and its message is not. A message from a
    /// file system carries the path the store sits at, and the settings page is
    /// where an operator is told which failure they have.
    /// </para>
    /// </remarks>
    private void FinishWhatWasLeftOpen()
    {
        try
        {
            _leftOpen.Run();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "The plays a previous run left open could not be finished, because the store could not be read: {Failure}. They stay on the file and the next start tries again. Recording was not affected.",
                ex.GetType().Name);
        }
    }

    private void OnPlaybackStart(object? sender, PlaybackProgressEventArgs args)
        => Guarded("playback start", () => args.PlaySessionId, () => _sink.PlaybackStarted(args));

    private void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs args)
        => Guarded("playback progress", () => args.PlaySessionId, () => _sink.PlaybackProgressed(args));

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs args)
        => Guarded("playback stop", () => args.PlaySessionId, () => _sink.PlaybackStopped(args));

    private void OnSessionEnded(object? sender, SessionEventArgs args)
        => Guarded("session end", () => args.SessionInfo.Id, () => _sink.SessionEnded(args));

    /// <summary>
    /// Runs a handler and swallows whatever it throws.
    /// </summary>
    /// <remarks>
    /// These handlers are invoked from the server's own event dispatch. An
    /// exception let out of one travels into the server, where the closest prior
    /// art for that is a plugin fault stopping playback bookkeeping for every
    /// other listener on the same event. A statistics plugin is not allowed to
    /// cost anybody a play, so a fault here ends here.
    /// <para>
    /// What is logged is the identifier of the play session and nothing else. A
    /// user name or an item title in a log line is personal detail in a file
    /// that is copied into bug reports and outlives every retention setting.
    /// </para>
    /// <para>
    /// The identifier is read inside the try rather than passed in already
    /// read. Reading it outside would put a dereference of the event's own
    /// fields on the far side of the catch, so an event that arrived malformed
    /// would throw where nothing is guarding, which is the one case this method
    /// exists for. It is also why neither this method nor its callers test
    /// anything for null: a null on that path is caught here, and a guard for
    /// it would be a branch no test can reach through the server's own
    /// dispatch.
    /// </para>
    /// </remarks>
    /// <param name="what">The event, for the log line.</param>
    /// <param name="playSessionId">Reads the play session the event belongs to.</param>
    /// <param name="handler">The work to run.</param>
    private void Guarded(string what, Func<string> playSessionId, Action handler)
    {
        string? identifier = null;

        try
        {
            identifier = playSessionId();
            handler();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Handling {What} for play session {PlaySessionId} failed. The event was dropped and the server was not affected.",
                what,
                identifier);
        }
    }
}
