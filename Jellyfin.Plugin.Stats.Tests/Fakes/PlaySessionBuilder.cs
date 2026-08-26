// Builds the session a play happens on. Every value a report is later grouped
// by is chosen here and nowhere else, so a test reads as a description of the
// play rather than as a pile of property assignments.
//
// What is deliberately absent: the address the session came from, and the
// library path of the item. Neither is a field this plugin keeps, and the
// invariant lint refuses both names in tracked C#.

using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// Builds a <see cref="SessionInfo"/> for a play and registers it with a
/// <see cref="FakeSessionManager"/>.
/// </summary>
public sealed class PlaySessionBuilder
{
    private readonly FakeSessionManager _sessions;

    private User? _user;
    private BaseItem? _item;
    private string _client = "Jellyfin Web";
    private string _deviceName = "A browser";
    private string _deviceId = "device-1";
    private string _sessionId = "session-1";
    private string _playSessionId = "play-1";
    private PlayMethod _playMethod = PlayMethod.DirectPlay;
    private TranscodingInfo? _transcoding;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaySessionBuilder"/> class.
    /// </summary>
    /// <param name="sessions">The manager the built session is added to.</param>
    public PlaySessionBuilder(FakeSessionManager sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    /// <summary>
    /// Sets the user the session belongs to.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder ForUser(User user)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        return this;
    }

    /// <summary>
    /// Sets the item being played.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder Playing(BaseItem item)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        return this;
    }

    /// <summary>
    /// Sets the client and the device the play comes from.
    /// </summary>
    /// <param name="client">The client name, as the server reports it.</param>
    /// <param name="deviceName">The device name.</param>
    /// <param name="deviceId">The device id.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder From(string client, string deviceName, string deviceId = "device-1")
    {
        _client = client;
        _deviceName = deviceName;
        _deviceId = deviceId;
        return this;
    }

    /// <summary>
    /// Gives the session and the play their own identifiers, for a test that
    /// needs more than one at a time.
    /// </summary>
    /// <param name="sessionId">The session id.</param>
    /// <param name="playSessionId">The play session id.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder Identified(string sessionId, string playSessionId)
    {
        _sessionId = sessionId;
        _playSessionId = playSessionId;
        return this;
    }

    /// <summary>
    /// Sets how the server is delivering the item.
    /// </summary>
    /// <param name="playMethod">The play method.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder Via(PlayMethod playMethod)
    {
        _playMethod = playMethod;
        return this;
    }

    /// <summary>
    /// Marks the play as transcoded and describes the transcode. Setting this
    /// also sets the play method, because a session that reports a transcode
    /// and a direct play at once is a state the server does not produce.
    /// </summary>
    /// <param name="videoCodec">The video codec being produced.</param>
    /// <param name="audioCodec">The audio codec being produced.</param>
    /// <param name="hardwareAcceleration">The hardware acceleration in use, if any.</param>
    /// <param name="reasons">Why the server is transcoding.</param>
    /// <param name="container">The container being produced.</param>
    /// <returns>This builder.</returns>
    public PlaySessionBuilder Transcoding(
        string videoCodec,
        string audioCodec,
        HardwareAccelerationType? hardwareAcceleration,
        TranscodeReason reasons,
        string container = "ts")
    {
        _transcoding = new TranscodingInfo
        {
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            Container = container,
            HardwareAccelerationType = hardwareAcceleration,
            TranscodeReasons = reasons,
            IsVideoDirect = false,
            IsAudioDirect = false
        };

        _playMethod = PlayMethod.Transcode;
        return this;
    }

    /// <summary>
    /// Builds the session and adds it to the manager this builder was made
    /// with, so the play can be raised on it straight away.
    /// </summary>
    /// <returns>The session.</returns>
    public SessionInfo Build()
    {
        if (_user is null)
        {
            throw new InvalidOperationException("A session belongs to a user. Call ForUser before Build.");
        }

        if (_item is null)
        {
            throw new InvalidOperationException("A play is a play of something. Call Playing before Build.");
        }

        var session = new SessionInfo(_sessions, NullLogger.Instance)
        {
            Id = _sessionId,
            UserId = _user.Id,
            UserName = _user.Username,
            Client = _client,
            DeviceId = _deviceId,
            DeviceName = _deviceName,
            PlaylistItemId = _playSessionId,
            FullNowPlayingItem = _item,
            TranscodingInfo = _transcoding
        };

        session.PlayState.PlayMethod = _playMethod;
        session.PlayState.PositionTicks = 0;

        _sessions.Add(session, _user);
        return session;
    }

    /// <summary>
    /// A video item with an id, a name and a runtime, which is everything a
    /// play needs from the library for these tests.
    /// </summary>
    /// <param name="name">The item name.</param>
    /// <param name="runtime">How long the item runs for.</param>
    /// <param name="id">The item id. A new one is made when this is omitted.</param>
    /// <returns>The item.</returns>
    public static BaseItem Video(string name, TimeSpan runtime, Guid? id = null)
    {
        return new MediaBrowser.Controller.Entities.Movies.Movie
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            RunTimeTicks = runtime.Ticks
        };
    }

    /// <summary>
    /// A live television programme, on a channel, with no runtime.
    /// </summary>
    /// <remarks>
    /// The programme carries the channel's identifier and no name for it, which
    /// is what the server hands a plugin: the name beside it is filled in for
    /// one source and live television is not that source. What the channel is
    /// called is declared on a <see cref="FakeChannelNames"/> instead.
    /// </remarks>
    /// <param name="name">The programme's name.</param>
    /// <param name="channelId">The channel it is on.</param>
    /// <param name="id">The item id. A new one is made when this is omitted.</param>
    /// <returns>The item.</returns>
    public static BaseItem LiveProgramme(string name, Guid channelId, Guid? id = null)
    {
        return new MediaBrowser.Controller.LiveTv.LiveTvProgram
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            ChannelId = channelId
        };
    }

    /// <summary>
    /// A live television channel, played as itself.
    /// </summary>
    /// <remarks>
    /// A channel carries no identifier of its own to resolve, so what a row
    /// records for it is the name it is already holding.
    /// </remarks>
    /// <param name="name">The channel's name.</param>
    /// <param name="id">The item id. A new one is made when this is omitted.</param>
    /// <returns>The item.</returns>
    public static BaseItem LiveChannel(string name, Guid? id = null)
    {
        return new MediaBrowser.Controller.LiveTv.LiveTvChannel
        {
            Id = id ?? Guid.NewGuid(),
            Name = name
        };
    }
}
