using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.Stats.Capture;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Takes a finished play off the server's thread and writes it from one of its
/// own.
/// </summary>
/// <remarks>
/// The plugin is handed its events from inside the server's own event dispatch,
/// so whatever the write path does is time the server spends before the next
/// listener on that event is reached. Opening a database file, waiting for a
/// lock and writing a row are all of that kind, and none of them belong on that
/// thread.
/// <para>
/// This sits behind the tracker rather than in front of it, which is the shape
/// the maintainer chose on issue #29. The tracker reads the live session the
/// event points at, including the moment the server last heard from it, so an
/// event queued and folded later is folded against a session that has moved on
/// and produces a row that is wrong. A finished row has already been read out of
/// those fields and holds still, so it is the thing that can wait.
/// </para>
/// <para>
/// What sits in the queue is therefore a row and never a sample. A progress
/// report is folded into memory by the tracker at the moment it arrives, which
/// is the same thread it arrived on, and it never reaches this class at all.
/// </para>
/// <para>
/// The store is opened by the writer thread on the first row rather than by
/// whoever constructed this, for the same reason: opening it is file work, and a
/// store that cannot be opened would otherwise throw where the server is
/// building its container. Here it is a failure of one row, counted and logged,
/// and the server keeps running. Issue #31 is where that state becomes visible
/// somewhere other than the log.
/// </para>
/// <para>
/// The four counters are what a test reads instead of a log line. Two of them
/// are kept by the caller's thread under the same lock the queue is guarded by
/// and two by the writer's own; all four are settled once
/// <see cref="Dispose"/> has returned, because that is the point at which the
/// writer thread has stopped and joined. Reading one while a play is in flight
/// reads a number that is moving.
/// </para>
/// </remarks>
public sealed class QueuedPlayWriter : IFinishedPlaySink, IDisposable
{
    /// <summary>
    /// How many finished rows may wait at once.
    /// </summary>
    /// <remarks>
    /// A row is a few hundred bytes and a busy server finishes a play every few
    /// seconds, so this is hours of backlog rather than seconds of it. It is
    /// named here rather than left to a caller because a bound chosen per call
    /// site is a bound nobody can state.
    /// </remarks>
    public const int DefaultBound = 1024;

    private const string TheQueueIsFull = "the queue is full";
    private const string TheWriterHasStopped = "the writer has stopped";

    private readonly BlockingCollection<PlayRecord> _pending;
    private readonly Func<IPlayStore> _openStore;
    private readonly ILogger<QueuedPlayWriter> _logger;
    private readonly HashSet<string> _alreadyReported = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _reportGate = new();
    private readonly Thread _writer;

    private bool _stopped;
    private int _accepted;
    private int _written;
    private int _refused;
    private int _failed;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueuedPlayWriter"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once, on the writer's own thread, when the first row is drained.</param>
    /// <param name="bound">How many rows may wait at once.</param>
    /// <param name="logger">The logger.</param>
    public QueuedPlayWriter(Func<IPlayStore> openStore, int bound, ILogger<QueuedPlayWriter> logger)
    {
        _openStore = openStore;
        _logger = logger;
        _pending = new BlockingCollection<PlayRecord>(bound);

        _writer = new Thread(Drain)
        {
            // A background thread does not hold the process open. The server
            // decides when it stops, and a plugin whose thread outlived it
            // would be a server that will not shut down.
            IsBackground = true,
            Name = "Playback statistics writer"
        };

        _writer.Start();
    }

    /// <summary>
    /// Gets how many rows this has taken responsibility for.
    /// </summary>
    public int Accepted => _accepted;

    /// <summary>
    /// Gets how many rows have reached the store.
    /// </summary>
    public int Written => _written;

    /// <summary>
    /// Gets how many rows were turned away without ever entering the queue.
    /// </summary>
    public int Refused => _refused;

    /// <summary>
    /// Gets how many rows the store would not take.
    /// </summary>
    public int Failed => _failed;

    /// <inheritdoc />
    /// <remarks>
    /// This returns without waiting for anything, which is the whole of what it
    /// is for. A full queue turns the row away rather than holding the caller
    /// until there is room: the caller is the server, and a plugin that makes
    /// the server wait for its own backlog has turned a slow report into a slow
    /// playback event.
    /// </remarks>
    /// <param name="play">The row the play came to.</param>
    public void Add(PlayRecord play)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                Turn(TheWriterHasStopped);
                return;
            }

            if (!_pending.TryAdd(play))
            {
                Turn(TheQueueIsFull);
                return;
            }

            _accepted++;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Every row already accepted is written before this returns. Accepting a
    /// row is a promise about that row, and a shutdown that dropped the backlog
    /// would break it on exactly the events a server produces most of, which is
    /// the ones near a restart.
    /// <para>
    /// The flag this reads is one of this class's own rather than the queue's
    /// <c>IsAddingCompleted</c>, and that is not a preference. The queue is
    /// disposed of at the end of this method and every one of its members
    /// throws afterwards, the one that reports it closed included. A second
    /// call and a play still in flight would both have arrived at that
    /// exception, which is a fault travelling back into the server from a
    /// plugin that has already finished. Both cases have a test.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            _pending.CompleteAdding();
        }

        _writer.Join();
        _pending.Dispose();
    }

    /// <summary>
    /// Writes rows until the queue is closed and empty.
    /// </summary>
    /// <remarks>
    /// One thread, so the store sees one writer and nothing here contends with
    /// itself. A row the store refuses is counted and dropped rather than
    /// retried: the next row is a different row, and a writer that stopped to
    /// retry the one in front of it would turn a single bad row into a stalled
    /// queue.
    /// </remarks>
    private void Drain()
    {
        IPlayStore? store = null;

        try
        {
            foreach (var play in _pending.GetConsumingEnumerable())
            {
                try
                {
                    store ??= _openStore();
                    store.Add(play);
                    _written++;
                }
                catch (Exception ex)
                {
                    _failed++;
                    ReportOnce(ex.GetType().FullName!, ex);
                }
            }
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>
    /// Counts a row that was turned away and says so at most once.
    /// </summary>
    /// <param name="why">The reason, which is also the occurrence class.</param>
    private void Turn(string why)
    {
        _refused++;
        ReportOnce(why, exception: null);
    }

    /// <summary>
    /// Writes one line for a class of failure and nothing for the next of the
    /// same class.
    /// </summary>
    /// <remarks>
    /// A store that cannot be written to fails on every row, and a line per row
    /// fills the server's log with one fault repeated. What an administrator
    /// needs is that it happened and what it was; the counters above carry how
    /// often, and they are the form a test can read.
    /// <para>
    /// The class is the exception's type, or the reason a row was turned away.
    /// A message would have been the finer grain and is the wrong one: a message
    /// carrying a row identifier is a new class per row, which is the flood this
    /// exists against.
    /// </para>
    /// </remarks>
    /// <param name="occurrenceClass">What kind of failure this is.</param>
    /// <param name="exception">The exception, where there was one.</param>
    private void ReportOnce(string occurrenceClass, Exception? exception)
    {
        lock (_reportGate)
        {
            if (!_alreadyReported.Add(occurrenceClass))
            {
                return;
            }
        }

        _logger.LogError(
            exception,
            "A finished play was not stored, because {OccurrenceClass}. Further plays lost the same way are counted and not logged.",
            occurrenceClass);
    }
}
