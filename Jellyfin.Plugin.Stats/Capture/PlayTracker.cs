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
/// A play that never stops stays open here as well as on the file. Closing one
/// is issue #221, which works in this type, and the reason it is not silently
/// discarded on session end is that discarding and recording an unfinished play
/// are different answers and that issue is where the choice is made.
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
    /// Nothing yet. A session ending while a play is open is a play that never
    /// received a stop, and issue #36 decides what becomes of it.
    /// </remarks>
    public void SessionEnded(SessionEventArgs args)
    {
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
        private readonly Data.PlayMethod _playMethod;

        private TrackedPlay(PlaybackProgressEventArgs args)
        {
            var item = args.Item;

            _userId = args.Users[0].Id;
            _itemId = item.Id;
            _itemType = item.GetBaseItemKind().ToString();
            _parentId = SeriesOf(item);
            _itemName = item.Name;
            _itemRuntime = RuntimeOf(item);
            _clientName = args.ClientName;
            _deviceId = args.DeviceId;
            _deviceName = args.DeviceName;
            _playMethod = MethodOf(args.Session.PlayState.PlayMethod);
            _startedUtc = HeardFrom(args);
            _watched = new WatchedTime(new DateTimeOffset(_startedUtc), PositionOf(args));
            _transcode.Observe(args.Session.TranscodingInfo);
        }

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
                PlayMethod = _playMethod,
                Transcode = _transcode.Finish()
            };
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
