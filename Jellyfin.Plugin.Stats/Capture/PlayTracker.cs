using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Joins the events of one play together and produces a row when it stops.
/// </summary>
/// <remarks>
/// A play arrives as a start, a series of progress reports and a stop, and
/// nothing in those events says they belong together except the identifier the
/// server carries on each of them. This holds the opening facts against that
/// identifier, folds each progress report into them, and hands one row to the
/// sink on the stop.
/// <para>
/// It reads no clock. Every moment it works from is the server's own record of
/// when it last heard from the session, which arrives on the event, so a play
/// an hour long is a test that runs in microseconds and the same events replayed
/// produce the same row. Issue #23 is the rule that refuses the other way.
/// </para>
/// <para>
/// A play that has started and not stopped is also handed to the sink, on the
/// start and again on every progress report, so it is on the file while it is
/// running rather than only once it is over. That is what makes a play the
/// server never finished a row somebody can still find, and it costs one row
/// per play however often the session reports, because the sink writes each one
/// under the key below and a key is a row. Issue #220.
/// </para>
/// <para>
/// A play that never stops is closed here rather than discarded, which is the
/// answer issue #221 asked for: an unfinished play is a play that happened, and
/// dropping it would make a server that was restarted look like a server nobody
/// watched anything on. Two routes reach it. A session that ends takes its open
/// plays with it, at the last moment the server heard from that session, and a
/// play whose session says nothing more is closed by
/// <see cref="CloseWhatHasGoneQuiet"/> once it has been quiet for longer than
/// the bound its caller names.
/// </para>
/// <para>
/// Both routes remove the play from this tracker in the same act that hands the
/// row over, so a stop arriving afterwards finds nothing open and is counted
/// rather than written. That is what keeps a play interrupted by anything to
/// exactly one row, which is the property the three pieces of issue #36 share.
/// </para>
/// <para>
/// Neither route reads a clock. The end of a closed play is the last moment the
/// server heard from its session, which arrived on an event, and the moment the
/// bound is measured against arrives as an argument. What a row written this way
/// does not yet say is which of the two routes closed it, and that column is
/// issue #222 rather than something omitted here.
/// </para>
/// </remarks>
public sealed class PlayTracker : IPlaybackEventSink
{
    /// <summary>
    /// The shape a row written today is written under. Issue #28 makes the
    /// store's migration series the authority for this number; until then it is
    /// the one the row shape was decided at.
    /// </summary>
    private const int RowSchemaVersion = 1;

    private readonly Dictionary<string, TrackedPlay> _open = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly IPlaySink _sink;
    private readonly ILogger<PlayTracker> _logger;
    private int _eventsWithNoOpenPlay;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayTracker"/> class.
    /// </summary>
    /// <param name="sink">Where a finished play is handed to.</param>
    /// <param name="logger">The logger.</param>
    public PlayTracker(IPlaySink sink, ILogger<PlayTracker> logger)
    {
        _sink = sink;
        _logger = logger;
    }

    /// <summary>
    /// Gets how many progress or stop events arrived for a play this tracker
    /// had no start for.
    /// </summary>
    /// <remarks>
    /// A count rather than only a log line, because the thing worth knowing is
    /// whether it happens at all and how often, and a number a test can read is
    /// the only form of that a check can refuse.
    /// </remarks>
    public int EventsWithNoOpenPlay
    {
        get
        {
            lock (_gate)
            {
                return _eventsWithNoOpenPlay;
            }
        }
    }

    /// <summary>
    /// Gets how many plays are open and waiting for a stop.
    /// </summary>
    public int OpenPlays
    {
        get
        {
            lock (_gate)
            {
                return _open.Count;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A start for a key that is already open replaces it. The same client
    /// starting the same item again is a new play, which is what the server
    /// reports and what a report should count, and the alternative of keeping
    /// the first would fold two viewings into one row.
    /// </remarks>
    public void PlaybackStarted(PlaybackProgressEventArgs args)
    {
        var play = TrackedPlay.From(args);
        var key = KeyOf(args);

        lock (_gate)
        {
            _open[key] = play;
        }

        // Outside the lock, like the finished row below and for the same
        // reason: the sink is the slowest thing on this path, and holding the
        // lock across it would make every other session's event wait behind
        // one write.
        _sink.NoteOpen(SoFar(key, play));
    }

    /// <inheritdoc />
    public void PlaybackProgressed(PlaybackProgressEventArgs args)
    {
        var key = KeyOf(args);
        TrackedPlay play;

        lock (_gate)
        {
            if (!_open.TryGetValue(key, out var found))
            {
                NoOpenPlay("playback progress", args);
                return;
            }

            found.Observe(args);
            play = found;
        }

        // The row is built outside the lock from a play only this session's
        // events touch, so what is written is what the fold above just
        // produced. A second report for the same key would have to come from
        // the same session, and the server sends one at a time.
        _sink.NoteOpen(SoFar(key, play));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The row is handed to the sink outside the lock. A sink that writes to a
    /// store is the slowest thing on this path, and holding the lock across it
    /// would make every other session's progress report wait behind one write.
    /// </remarks>
    public void PlaybackStopped(PlaybackStopEventArgs args)
    {
        PlayRecord row;
        var key = KeyOf(args);

        lock (_gate)
        {
            if (!_open.TryGetValue(key, out var play))
            {
                NoOpenPlay("playback stop", args);
                return;
            }

            _open.Remove(key);
            play.Observe(args);
            row = play.Finish(args.PlayedToCompletion);
        }

        // The key travels with the row, so the finished row arriving and the
        // running row going are one act rather than two. Apart, they are how
        // one play becomes two: a process that stopped between them would leave
        // both rows, and whatever finishes what a restart left open would write
        // the play again.
        _sink.Add(row, key);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A session that ends while a play is open is a play that will never
    /// receive a stop, so the play is closed at the last moment the server heard
    /// from that session and handed over as a finished row.
    /// <para>
    /// The plays are found by the session they arrived on rather than by the key
    /// their events are joined on. The two are different identifiers, one
    /// session can be playing more than one thing where a client queues, and a
    /// key that stood in for a missing play session identifier is built from a
    /// device and an item and carries no session at all. Matching on the session
    /// is what closes every play the ended session held and nothing belonging to
    /// anybody else.
    /// </para>
    /// <para>
    /// Nothing here claims the item was played through. The server says that on
    /// the stop, and this is the case where no stop came.
    /// </para>
    /// </remarks>
    public void SessionEnded(SessionEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Close(play => string.Equals(play.SessionId, args.SessionInfo.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Closes every open play whose session has said nothing for longer than
    /// the bound, and produces a row for each.
    /// </summary>
    /// <remarks>
    /// What a session that stopped reporting without ending leaves behind: a
    /// client that lost its network, a device that was switched off, a browser
    /// tab that was closed hard. Nothing further is coming for those plays, and
    /// left alone they sit open until the process stops.
    /// <para>
    /// The moment arrives as an argument and is never read from a machine clock,
    /// which is what <c>no-ambient-clock</c> in <c>tools/invariants/rules</c>
    /// refuses the other way and what lets a test choose a play an hour old
    /// without waiting an hour. What supplies it on a server is
    /// <see cref="ScheduledTasks.QuietPlaySweep"/>.
    /// </para>
    /// <para>
    /// The comparison is against the last moment the server heard from the
    /// session, which is the end of the row the play would produce now. A play
    /// being watched reports while it runs and while it is paused, so a bound
    /// shorter than the interval a client checks in at would close plays that
    /// are still running, which is why the value is a constant somebody has to
    /// change on purpose rather than a setting.
    /// </para>
    /// </remarks>
    /// <param name="now">The moment the bound is measured back from.</param>
    /// <param name="bound">How long a play may hear nothing before it is closed.</param>
    /// <returns>How many plays were closed.</returns>
    public int CloseWhatHasGoneQuiet(DateTimeOffset now, TimeSpan bound)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bound, TimeSpan.Zero);

        var quietBefore = now.UtcDateTime - bound;

        return Close(play => play.LastHeardFromUtc <= quietBefore);
    }

    /// <summary>
    /// Closes every open play the test picks out, and hands each of their rows
    /// to the sink.
    /// </summary>
    /// <remarks>
    /// The plays are taken out of the dictionary under the lock and the rows go
    /// to the sink outside it, which is what the stop path does and for the same
    /// reason: the sink is the slowest thing here, and holding the lock across
    /// it would make every other session's event wait behind a write.
    /// <para>
    /// Taking them out is not a detail of the locking. A play handed over is a
    /// play that has produced its row, and one left in the dictionary would
    /// produce a second one the moment a late stop arrived.
    /// </para>
    /// </remarks>
    /// <param name="wanted">Which open plays to close.</param>
    /// <returns>How many were closed.</returns>
    private int Close(Func<TrackedPlay, bool> wanted)
    {
        List<KeyValuePair<string, PlayRecord>> closed;

        lock (_gate)
        {
            var keys = new List<string>();
            foreach (var (key, play) in _open)
            {
                if (wanted(play))
                {
                    keys.Add(key);
                }
            }

            closed = new List<KeyValuePair<string, PlayRecord>>(keys.Count);
            foreach (var key in keys)
            {
                var play = _open[key];
                _open.Remove(key);
                closed.Add(new KeyValuePair<string, PlayRecord>(key, play.Finish(reachedTheEnd: false)));
            }
        }

        foreach (var (key, row) in closed)
        {
            _sink.Add(row, key);
        }

        return closed.Count;
    }

    /// <summary>
    /// The identifier the events of one play are joined on.
    /// </summary>
    /// <remarks>
    /// The server carries a play session identifier on each event and that is
    /// the value, because it distinguishes two clients playing one item at the
    /// same moment and it changes when a client starts the item again. Where it
    /// is absent the device and the item together stand in: one device plays one
    /// item once at a time, so the pair is unique among the plays that are open,
    /// which is all this key has to be.
    /// </remarks>
    private static string KeyOf(PlaybackProgressEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PlaySessionId))
        {
            return string.Concat(args.DeviceId, " ", args.Item.Id.ToString("N", CultureInfo.InvariantCulture));
        }

        return args.PlaySessionId;
    }

    private void NoOpenPlay(string what, PlaybackProgressEventArgs args)
    {
        _eventsWithNoOpenPlay++;

        // The identifier and nothing else. A user name or an item title in a
        // log line is personal detail in a file that is copied into bug reports
        // and outlives every retention setting.
        _logger.LogWarning(
            "A {What} arrived for play session {PlaySessionId} with no start this plugin saw. No row was written.",
            what,
            args.PlaySessionId);
    }

    /// <summary>
    /// The play as the file should hold it while it is still running.
    /// </summary>
    /// <remarks>
    /// The same row a stop would produce, with the two fields that cannot be
    /// known yet reading as <see cref="OpenPlay"/> says they do: the end is the
    /// last moment the server heard from the session, and nothing claims the
    /// item was played through.
    /// </remarks>
    /// <param name="key">The key the play's events are joined on.</param>
    /// <param name="play">The play so far.</param>
    /// <returns>The open row.</returns>
    private static OpenPlay SoFar(string key, TrackedPlay play)
        => new() { PlayKey = key, SoFar = play.Finish(reachedTheEnd: false) };

    /// <summary>
    /// One play that has started and not yet stopped.
    /// </summary>
    private sealed class TrackedPlay
    {
        private readonly WatchedTime _watched;
        private readonly TranscodeFold _transcode = new();
        private readonly Guid _userId;
        private readonly Guid _itemId;
        private readonly string _itemType;
        private readonly Guid? _parentId;
        private readonly string _itemName;
        private readonly TimeSpan? _itemRuntime;
        private readonly DateTime _startedUtc;
        private readonly string _clientName;
        private readonly string _deviceId;
        private readonly string _deviceName;
        private readonly Data.PlayMethod _playMethodAtStart;
        private readonly string _sessionId;

        private DateTime? _playMethodChangedUtc;

        private TrackedPlay(PlaybackProgressEventArgs args)
        {
            var item = args.Item;

            _sessionId = args.Session.Id;
            _userId = args.Users[0].Id;
            _itemId = item.Id;
            _itemType = item.GetBaseItemKind().ToString();
            _parentId = SeriesOf(item);
            _itemName = item.Name;
            _itemRuntime = RuntimeOf(item);
            _clientName = args.ClientName;
            _deviceId = args.DeviceId;
            _deviceName = args.DeviceName;
            _playMethodAtStart = MethodOf(args.Session.PlayState.PlayMethod);
            _startedUtc = HeardFrom(args);
            _watched = new WatchedTime(new DateTimeOffset(_startedUtc), PositionOf(args));
            _transcode.Observe(args.Session.TranscodingInfo);
        }

        /// <summary>
        /// Gets the session this play arrived on.
        /// </summary>
        /// <remarks>
        /// Read off the start event and never moved. A play belongs to the
        /// session it began on, and this is what a session ending is matched
        /// against; the key the play's events are joined on is a different
        /// identifier and answers a different question.
        /// </remarks>
        public string SessionId => _sessionId;

        /// <summary>
        /// Gets the last moment the server heard from this play's session.
        /// </summary>
        /// <remarks>
        /// The same moment the row would carry as its end, derived the same way,
        /// rather than a second field that could fall out of step with it. It is
        /// what a bound measured against a moment somebody supplies is compared
        /// with.
        /// </remarks>
        public DateTime LastHeardFromUtc => _startedUtc + _watched.WallClock;

        /// <summary>
        /// Opens a play from the start event that began it.
        /// </summary>
        /// <param name="args">The start event.</param>
        /// <returns>The open play.</returns>
        public static TrackedPlay From(PlaybackProgressEventArgs args) => new(args);

        /// <summary>
        /// Folds one progress report, or the stop, into the play.
        /// </summary>
        /// <remarks>
        /// The transcoding state is sampled here rather than stored, so the
        /// number of rows a play produces does not move with how long it ran or
        /// how often the client reported.
        /// </remarks>
        /// <param name="args">The event.</param>
        public void Observe(PlaybackProgressEventArgs args)
        {
            _watched.Observe(new DateTimeOffset(HeardFrom(args)), PositionOf(args), args.IsPaused);
            _transcode.Observe(args.Session.TranscodingInfo);
            NoticeAMethodChange(args);
        }

        /// <summary>
        /// Closes the play and produces its row.
        /// </summary>
        /// <param name="reachedTheEnd">Whether the server said the item was played to completion.</param>
        /// <returns>The row.</returns>
        public PlayRecord Finish(bool reachedTheEnd)
        {
            return new PlayRecord
            {
                SchemaVersion = RowSchemaVersion,
                UserId = _userId,
                ItemId = _itemId,
                ItemType = _itemType,
                ParentId = _parentId,
                ItemName = _itemName,
                ItemRuntime = _itemRuntime,
                StartedUtc = _startedUtc,

                // Not the moment the stop arrived. The field the times come from
                // is the server's own record of when it last heard from the
                // session, and the server does not touch it when a play stops,
                // so the honest end of a play is the last contact it had. Adding
                // the wall clock the fold already holds says exactly that and
                // cannot land before the start, whatever order the events
                // arrived in.
                EndedUtc = _startedUtc + _watched.WallClock,
                WatchedDuration = _watched.Watched,
                ReachedTheEnd = reachedTheEnd,
                ClientName = _clientName,
                DeviceId = _deviceId,
                DeviceName = _deviceName,
                PlayMethodAtStart = _playMethodAtStart,
                PlayMethodChangedUtc = _playMethodChangedUtc,
                Transcode = _transcode.Finish()
            };
        }

        /// <summary>
        /// Records the first moment the server reported a delivery method other
        /// than the one the play began with.
        /// </summary>
        /// <remarks>
        /// The first and not the last. What a reader needs is whether the start
        /// value still described the play and from when it did not, and a play
        /// that moved twice would have neither answered by the last move.
        /// <para>
        /// A sample the server gave no method for is skipped rather than read
        /// as a move to unknown, the same way a sample it gave no transcoding
        /// state for leaves the summary alone. The server having nothing to say
        /// about a session is not the session having changed, and a play whose
        /// client goes quiet for one report would otherwise be recorded as one
        /// whose delivery changed twice.
        /// </para>
        /// <para>
        /// A play that began before the server had decided is the case this is
        /// least obvious for and it is deliberate: the start value is unknown,
        /// the first method the server names is not that, and the moment is
        /// recorded. The row then says the start value never described the play
        /// and from when, which is exactly what a reader comparing it against
        /// the transcode summary has to know.
        /// </para>
        /// </remarks>
        /// <param name="args">The event.</param>
        private void NoticeAMethodChange(PlaybackProgressEventArgs args)
        {
            if (_playMethodChangedUtc is not null)
            {
                return;
            }

            if (args.Session.PlayState.PlayMethod is not { } reported)
            {
                return;
            }

            if (MethodOf(reported) == _playMethodAtStart)
            {
                return;
            }

            _playMethodChangedUtc = HeardFrom(args);
        }

        /// <summary>
        /// When the server last heard from the session the event arrived on.
        /// </summary>
        /// <remarks>
        /// This is where every moment in a row comes from, and it is read off
        /// the session rather than taken from a clock in this process. The
        /// server sets it in one place, when a client checks in, so it only ever
        /// moves forward.
        /// </remarks>
        private static DateTime HeardFrom(PlaybackProgressEventArgs args)
            => DateTime.SpecifyKind(args.Session.LastPlaybackCheckIn, DateTimeKind.Utc);

        private static TimeSpan PositionOf(PlaybackProgressEventArgs args)
            => TimeSpan.FromTicks(args.PlaybackPositionTicks.GetValueOrDefault());

        private static TimeSpan? RuntimeOf(BaseItem item)
        {
            if (item.RunTimeTicks is { } ticks)
            {
                return TimeSpan.FromTicks(ticks);
            }

            return null;
        }

        /// <summary>
        /// The series an item belongs to, and null where it belongs to none.
        /// </summary>
        /// <remarks>
        /// An episode carries the series it is under, which is what a report
        /// groups on; the season it sits in is not that. An item with no series
        /// at all, and one whose series the server did not fill in, are both
        /// null rather than an empty identifier that reads as a real parent.
        /// </remarks>
        private static Guid? SeriesOf(BaseItem item)
        {
            if (item is IHasSeries series && series.SeriesId != Guid.Empty)
            {
                return series.SeriesId;
            }

            return null;
        }

        /// <summary>
        /// The server's delivery method as this plugin's own closed set.
        /// </summary>
        /// <remarks>
        /// A value the server adds later, and a session that reported none, both
        /// come out as unknown rather than as one of the three, so a row never
        /// claims a delivery method nobody reported.
        /// </remarks>
        private static Data.PlayMethod MethodOf(ServerPlayMethod? method)
        {
            return method switch
            {
                ServerPlayMethod.DirectPlay => Data.PlayMethod.DirectPlay,
                ServerPlayMethod.DirectStream => Data.PlayMethod.DirectStream,
                ServerPlayMethod.Transcode => Data.PlayMethod.Transcode,
                _ => Data.PlayMethod.Unknown
            };
        }
    }
}
