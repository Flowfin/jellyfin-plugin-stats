using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.Stats.Capture;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Takes a play off the server's thread and writes it from one of its own.
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
/// is the same thread it arrived on; what reaches this class is the play as it
/// stands after that fold, which is a row of the same shape as the finished
/// one.
/// </para>
/// <para>
/// Three things can be queued and all three are one row's worth of work: the
/// play as it stands while it is running, the play once it has stopped, and the
/// taking away of a running play no finished row is coming for. They share one
/// queue and one thread, so the store sees one writer and the order a play's
/// own events were queued in is the order they reach the file. Two queues would
/// have let a stop overtake the last progress report of the play it ends, which
/// would put the open row back after the finished one had taken it away. Issue
/// #220.
/// </para>
/// <para>
/// The store is opened by the writer thread on the first row rather than by
/// whoever constructed this, for the same reason: opening it is file work, and a
/// store that cannot be opened would otherwise throw where the server is
/// building its container. Here it is a failure of one row, counted and logged,
/// and the server keeps running.
/// </para>
/// <para>
/// <see cref="WhyTheStoreCouldNotBeOpened"/> is that failure said out loud
/// rather than left in the log, which is what the first two conditions of issue
/// #31 ask for. It answers for the store and not for the queue: a row this
/// refused because the queue was full is a busy plugin and not a broken one.
/// </para>
/// <para>
/// The four counters are what a test reads instead of a log line, and the
/// statement above is read the same way. Two of the counters are kept by the
/// caller's thread under the same lock the queue is guarded by and two by the
/// writer's own; all four are settled once <see cref="Dispose"/> has returned,
/// because that is the point at which the writer thread has stopped and joined.
/// Reading one while a play is in flight reads a number that is moving.
/// </para>
/// <para>
/// <see cref="WaitUntilNothingIsWaiting"/> is the other moment at which they
/// hold still, and unlike <see cref="Dispose"/> it leaves the writer running.
/// It answers for what was queued before it was called and for nothing queued
/// after, which is the whole of what a caller may read out of it.
/// </para>
/// </remarks>
public sealed class QueuedPlayWriter : IPlaySink, IDisposable
{
    /// <summary>
    /// How many finished rows may wait at once.
    /// </summary>
    /// <remarks>
    /// A row is a few hundred bytes and a busy server finishes a play every few
    /// seconds, so this is hours of backlog rather than seconds of it. It is
    /// named here rather than left to a caller because a bound chosen per call
    /// site is a bound nobody can state.
    /// <para>
    /// A progress report costs a place in it as well now, so the backlog this
    /// covers is shorter in hours than it was. It is still hours: a session
    /// reports on a timer measured in tens of seconds, and this is a thousand
    /// places.
    /// </para>
    /// </remarks>
    public const int DefaultBound = 1024;

    private const string TheQueueIsFull = "the queue is full";
    private const string TheWriterHasStopped = "the writer has stopped";

    private readonly BlockingCollection<Pending> _pending;
    private readonly Func<IPlayStore> _openStore;
    private readonly ILogger<QueuedPlayWriter> _logger;
    private readonly HashSet<string> _alreadyReported = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly object _reportGate = new();
    private readonly Thread _writer;

    // Set exactly while the queue is empty and the writer is between rows, so
    // a caller that queued something and then waits on this is waiting for
    // that row to have reached the store. Both sides are taken under _gate:
    // the producer resets it inside the same held lock that adds the row, and
    // the writer takes the lock to ask whether the queue emptied, so there is
    // no window in which a row is queued and this still says nothing waits.
    private readonly ManualResetEventSlim _nothingIsWaiting = new(true);

    private bool _stopped;
    private int _accepted;
    private int _written;
    private int _refused;
    private int _failed;

    // Written by the writer thread and read by whoever asks, so the read has to
    // see the last write rather than one the compiler kept in a register. A
    // reference field may be volatile; the counters beside it may not, which is
    // why they are not.
    private volatile string? _whyTheStoreCouldNotBeOpened;

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
        _pending = new BlockingCollection<Pending>(bound);

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
    /// Gets how many pieces of work this has taken responsibility for.
    /// </summary>
    /// <remarks>
    /// Every queued thing rather than every finished play, because what this
    /// number is about is the queue: it is the count that
    /// <see cref="Refused"/> is the other half of, and a place in the queue is
    /// a place whatever is standing in it.
    /// </remarks>
    public int Accepted => _accepted;

    /// <summary>
    /// Gets how many finished plays have reached the store.
    /// </summary>
    /// <remarks>
    /// Finished plays and not writes. A running play is written to the file
    /// again on every progress report, so a counter that took those in would
    /// move with how often a session checked in, which is the thing this
    /// plugin's file size deliberately does not do and the last number that
    /// should. What it counts is plays that are over, which is what it counted
    /// before there was a second kind of write.
    /// </remarks>
    public int Written => _written;

    /// <summary>
    /// Gets how many rows were turned away without ever entering the queue.
    /// </summary>
    public int Refused => _refused;

    /// <summary>
    /// Gets how many rows the store would not take.
    /// </summary>
    public int Failed => _failed;

    /// <summary>
    /// Gets why the store could not be opened, and null while nothing says it
    /// could not. This is the plugin saying it cannot store anything, which is
    /// a different statement from having stored nothing.
    /// </summary>
    /// <remarks>
    /// It is the last attempt and not a verdict. The writer opens the store
    /// when a row arrives and it has none, so a file that was locked and is now
    /// free opens on the next play and this goes back to null without anybody
    /// restarting the server. A state that could only be left by a restart
    /// would turn a lock somebody cleared in a minute into a plugin that stays
    /// off until the next one.
    /// <para>
    /// The value is the exception's type, which is the same class the log line
    /// carries, so the two name one thing. It is not a sentence written for a
    /// reader: turning it into words belongs where it is shown, and the third
    /// condition of issue #31 is where that lands. Nothing shows it yet, because
    /// a page reads what the plugin knows over a route this tree does not have,
    /// which is the same absence issue #65 records.
    /// </para>
    /// <para>
    /// A row the store refused after it opened does not set this. Those are
    /// counted by <see cref="Failed"/> and are a different failure: one row the
    /// store would not take is a bad row, and a plugin that switched itself off
    /// over one of them would lose every play after it for a reason that had
    /// already passed.
    /// </para>
    /// </remarks>
    public string? WhyTheStoreCouldNotBeOpened => _whyTheStoreCouldNotBeOpened;

    /// <inheritdoc />
    /// <remarks>
    /// This returns without waiting for anything, which is the whole of what it
    /// is for. A full queue turns the row away rather than holding the caller
    /// until there is room: the caller is the server, and a plugin that makes
    /// the server wait for its own backlog has turned a slow report into a slow
    /// playback event.
    /// </remarks>
    /// <param name="play">The row the play came to.</param>
    /// <param name="playKey">The key the play's events were joined on.</param>
    public void Add(PlayRecord play, string playKey)
        => Queue(new Pending(play, playKey, OpenPlayKey: null));

    /// <inheritdoc />
    /// <remarks>
    /// Turned away when the queue is full, like a finished row. The next
    /// progress report of the same play carries everything this one did, so
    /// what is lost by dropping one is how far a play had got at a moment that
    /// has already passed, which is the cheapest thing in the queue to lose.
    /// </remarks>
    /// <param name="play">The play as it stands.</param>
    public void NoteOpen(OpenPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);

        Queue(new Pending(play.SoFar, PlayKey: null, OpenPlayKey: play.PlayKey));
    }

    /// <inheritdoc />
    /// <param name="playKey">The key the play's events were joined on.</param>
    public void ForgetOpen(string playKey)
        => Queue(new Pending(Finished: null, PlayKey: null, OpenPlayKey: playKey));

    /// <summary>
    /// Waits until nothing this took responsibility for is still waiting to be
    /// done to the store, and says whether that happened inside the wait.
    /// </summary>
    /// <remarks>
    /// This is the fact <see cref="Dispose"/> already establishes, offered
    /// without stopping the writer. Everything queued before the call returns
    /// true has reached the store or has been counted as failing to; what is
    /// queued after the call is nothing this answers for, so a caller queues
    /// first and asks second.
    /// <para>
    /// The bound is on how long one queued row may take to reach the file. It
    /// is not a bound on how many times anything is looked at, because nothing
    /// here looks: the writer sets the signal as it finishes a row, so a caller
    /// that wakes has been told rather than having found out. A reader polling
    /// the file for the same answer opens a second connection to it every few
    /// milliseconds, which is contention with the write it is waiting for, and
    /// issue #241 is where that was measured.
    /// </para>
    /// <para>
    /// A writer thread that has stopped without emptying the queue leaves this
    /// waiting until the bound runs out, deliberately. Reporting a queue nobody
    /// will drain as drained would turn a plugin that has stopped writing into
    /// a caller that believes its row landed, and a test built on that would
    /// pass over exactly the failure it exists for.
    /// </para>
    /// </remarks>
    /// <param name="howLongToWait">How long to wait before giving up.</param>
    /// <returns>True where the queue emptied inside the wait.</returns>
    public bool WaitUntilNothingIsWaiting(TimeSpan howLongToWait)
        => _nothingIsWaiting.Wait(howLongToWait);

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
        _nothingIsWaiting.Dispose();
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
    /// <para>
    /// Opening and writing are caught separately, and that is not tidiness. A
    /// store that cannot be opened is the plugin unable to keep anything, and a
    /// row the store refused once it was open is one row. Caught together they
    /// are one number and one log class, and the only way left to tell them
    /// apart is to read the exception type and know which one it means.
    /// </para>
    /// </remarks>
    private void Drain()
    {
        IPlayStore? store = null;

        try
        {
            foreach (var pending in _pending.GetConsumingEnumerable())
            {
                // A row is finished with when this iteration ends, however it
                // ended. The store that would not open and the row the store
                // refused are both done being waited for: they will not be
                // retried, so a caller held until they succeeded would be held
                // for ever.
                try
                {
                    if (store is null)
                    {
                        try
                        {
                            store = _openStore();
                            _whyTheStoreCouldNotBeOpened = null;
                        }
                        catch (Exception ex)
                        {
                            _failed++;
                            _whyTheStoreCouldNotBeOpened = ex.GetType().FullName!;
                            ReportOnce(ex.GetType().FullName!, ex);
                            continue;
                        }
                    }

                    try
                    {
                        Write(store, pending);

                        if (pending.PlayKey is not null)
                        {
                            _written++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _failed++;
                        ReportOnce(ex.GetType().FullName!, ex);
                    }
                }
                finally
                {
                    SayWhetherAnythingIsStillWaiting();
                }
            }
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>
    /// Does one queued thing to the store.
    /// </summary>
    /// <remarks>
    /// The three cases are told apart by which of the two keys is there, and
    /// the record that carries them refuses any other combination when it is
    /// built, so nothing here has a fourth branch nobody could reach.
    /// </remarks>
    /// <param name="store">The open store.</param>
    /// <param name="pending">What was queued.</param>
    private static void Write(IPlayStore store, Pending pending)
    {
        if (pending.Finished is null)
        {
            store.ForgetOpenPlay(pending.OpenPlayKey!);
            return;
        }

        if (pending.OpenPlayKey is not null)
        {
            store.NoteOpenPlay(new OpenPlay { PlayKey = pending.OpenPlayKey, SoFar = pending.Finished });
            return;
        }

        store.AddAndForgetOpenPlay(pending.Finished, pending.PlayKey!);
    }

    /// <summary>
    /// Says nothing is waiting, where the queue has come to be empty.
    /// </summary>
    /// <remarks>
    /// Taken under the same lock a producer holds while it adds a row, so the
    /// question and the addition cannot interleave. Asked after a row is
    /// finished with rather than after it is taken off the queue: between those
    /// two moments the queue is already empty and the row has not reached the
    /// file, which is the one reading that would be wrong.
    /// </remarks>
    private void SayWhetherAnythingIsStillWaiting()
    {
        lock (_gate)
        {
            if (_pending.Count == 0)
            {
                _nothingIsWaiting.Set();
            }
        }
    }

    /// <summary>
    /// Puts one thing on the queue, or counts it as turned away.
    /// </summary>
    /// <remarks>
    /// This returns without waiting for anything, which is the whole of what
    /// the class is for. A full queue turns the work away rather than holding
    /// the caller until there is room: the caller is the server, and a plugin
    /// that makes the server wait for its own backlog has turned a slow report
    /// into a slow playback event.
    /// </remarks>
    /// <param name="pending">What to do.</param>
    private void Queue(Pending pending)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                Turn(TheWriterHasStopped);
                return;
            }

            if (!_pending.TryAdd(pending))
            {
                Turn(TheQueueIsFull);
                return;
            }

            _accepted++;
            _nothingIsWaiting.Reset();
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
            "A play was not stored, because {OccurrenceClass}. Further plays lost the same way are counted and not logged.",
            occurrenceClass);
    }

    /// <summary>
    /// One thing waiting to be done to the store.
    /// </summary>
    /// <remarks>
    /// Three shapes in one type rather than three queues, because one queue is
    /// what keeps a play's own events in the order they happened. Which shape
    /// an entry is, is decided by the two keys, and the three places that build
    /// one are the three methods above, each of which names its shape in one
    /// line.
    /// <para>
    /// Nothing is refused here. The store refuses a key that is empty and a
    /// play that is null, at the moment it would write one, which is a refusal
    /// a case can reach; a second one in this record would be a branch no route
    /// could take and no proof could bite on.
    /// </para>
    /// </remarks>
    /// <param name="Finished">The play, and null where an open row is only being taken away.</param>
    /// <param name="PlayKey">The key of a stop, and null otherwise.</param>
    /// <param name="OpenPlayKey">The key of a play still running, or of one being taken away, and null on a stop.</param>
    private sealed record Pending(PlayRecord? Finished, string? PlayKey, string? OpenPlayKey);
}
