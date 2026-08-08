// A session manager the tests own, so a play can be raised without a server.
//
// Every member of ISessionManager is here because C# requires it, and almost
// all of them refuse. That is the point of the shape: the members that do
// something are the surface this plugin depends on, and they fit on one screen
// at the top of the file. A member that starts doing something is a widening of
// that surface, and it shows up in a diff as one.
//
// Nothing here sets the network address a session came from. It is not a field
// this plugin reads, and the invariant lint refuses its name in tracked C#, so
// this sentence cannot spell it either.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Entities.Security;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Session;
using MediaBrowser.Model.SyncPlay;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// An <see cref="ISessionManager"/> that raises playback events on demand and
/// refuses everything a server would otherwise do.
/// </summary>
public sealed class FakeSessionManager : ISessionManager
{
    private readonly List<SessionInfo> _sessions = new();
    private readonly Dictionary<string, User> _usersBySession = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event EventHandler<PlaybackProgressEventArgs>? PlaybackStart;

    /// <inheritdoc />
    public event EventHandler<PlaybackProgressEventArgs>? PlaybackProgress;

    /// <inheritdoc />
    public event EventHandler<PlaybackStopEventArgs>? PlaybackStopped;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionStarted;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionEnded;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionActivity;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? SessionControllerConnected;

    /// <inheritdoc />
    public event EventHandler<SessionEventArgs>? CapabilitiesChanged;

    /// <inheritdoc />
    public IEnumerable<SessionInfo> Sessions => _sessions;

    /// <summary>
    /// Adds a session to the ones this manager reports, and records which user
    /// it belongs to. The server carries that on the event rather than on the
    /// session, so the fake has to be told once.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="user">The user the session belongs to.</param>
    public void Add(SessionInfo session, User user)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(user);

        _sessions.Add(session);
        _usersBySession[session.Id] = user;
    }

    /// <summary>
    /// Raises playback start for a session, as the server does when a client
    /// begins playing.
    /// </summary>
    /// <param name="session">The session that started playing.</param>
    /// <param name="at">When the server heard from the session, where the test means a particular moment.</param>
    public void RaisePlaybackStart(SessionInfo session, DateTimeOffset? at = null)
    {
        HeardFrom(session, at);

        // Built before the invoke rather than inside it: a null-conditional
        // invoke does not evaluate its argument, and a fake that only checks
        // its inputs when somebody is listening is a fake that checks nothing.
        var args = Progress(session, TimeSpan.Zero, isPaused: false);
        PlaybackStart?.Invoke(this, args);
    }

    /// <summary>
    /// Raises a playback progress report for a session.
    /// </summary>
    /// <param name="session">The session that reported.</param>
    /// <param name="position">How far into the item the session is.</param>
    /// <param name="isPaused">Whether the session is paused at that position.</param>
    /// <param name="at">When the server heard from the session, where the test means a particular moment.</param>
    public void RaisePlaybackProgress(SessionInfo session, TimeSpan position, bool isPaused = false, DateTimeOffset? at = null)
    {
        HeardFrom(session, at);
        var args = Progress(session, position, isPaused);
        PlaybackProgress?.Invoke(this, args);
    }

    /// <summary>
    /// Raises playback stop for a session.
    /// </summary>
    /// <param name="session">The session that stopped.</param>
    /// <param name="position">Where the session stopped.</param>
    /// <param name="playedToCompletion">Whether the item was played to the end.</param>
    /// <param name="at">When the server last heard from the session.</param>
    public void RaisePlaybackStopped(SessionInfo session, TimeSpan position, bool playedToCompletion = false, DateTimeOffset? at = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        HeardFrom(session, at);

        var args = new PlaybackStopEventArgs
        {
            Session = session,
            Item = session.FullNowPlayingItem,
            Users = UsersOf(session),
            DeviceId = session.DeviceId,
            DeviceName = session.DeviceName,
            ClientName = session.Client,
            PlaySessionId = session.PlaylistItemId,
            PlaybackPositionTicks = position.Ticks,
            PlayedToCompletion = playedToCompletion
        };

        PlaybackStopped?.Invoke(this, args);
    }

    /// <summary>
    /// Raises the session lifecycle events a session goes through, for a test
    /// that needs one without a play.
    /// </summary>
    /// <param name="session">The session.</param>
    public void RaiseSessionStarted(SessionInfo session)
    {
        SessionStarted?.Invoke(this, new SessionEventArgs { SessionInfo = session });
    }

    /// <summary>
    /// Raises session end for a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void RaiseSessionEnded(SessionInfo session)
    {
        SessionEnded?.Invoke(this, new SessionEventArgs { SessionInfo = session });
    }

    /// <summary>
    /// Raises session activity for a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void RaiseSessionActivity(SessionInfo session)
    {
        SessionActivity?.Invoke(this, new SessionEventArgs { SessionInfo = session });
    }

    /// <summary>
    /// Raises controller connected for a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void RaiseSessionControllerConnected(SessionInfo session)
    {
        SessionControllerConnected?.Invoke(this, new SessionEventArgs { SessionInfo = session });
    }

    /// <summary>
    /// Raises a capabilities change for a session.
    /// </summary>
    /// <param name="session">The session.</param>
    public void RaiseCapabilitiesChanged(SessionInfo session)
    {
        CapabilitiesChanged?.Invoke(this, new SessionEventArgs { SessionInfo = session });
    }

    /// <summary>
    /// Sets the moment the server last heard from the session, which is where
    /// every time in a play row comes from.
    /// </summary>
    /// <remarks>
    /// The server writes this field when a client checks in, and only then, so
    /// a test that means a play an hour long says so here rather than waiting an
    /// hour or letting anything read a clock. A raise that names no moment
    /// leaves the field where it was, so the tests written before this existed
    /// see exactly what they saw.
    /// </remarks>
    private static void HeardFrom(SessionInfo session, DateTimeOffset? at)
    {
        if (at is { } moment)
        {
            session.LastPlaybackCheckIn = moment.UtcDateTime;
        }
    }

    private List<User> UsersOf(SessionInfo session)
    {
        if (_usersBySession.TryGetValue(session.Id, out var user))
        {
            return new List<User> { user };
        }

        throw new InvalidOperationException(
            "The session was never added to this manager, so the user it belongs to is not known. Call Add(session, user) first.");
    }

    private PlaybackProgressEventArgs Progress(SessionInfo session, TimeSpan position, bool isPaused)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new PlaybackProgressEventArgs
        {
            Session = session,
            Item = session.FullNowPlayingItem,
            Users = UsersOf(session),
            DeviceId = session.DeviceId,
            DeviceName = session.DeviceName,
            ClientName = session.Client,
            PlaySessionId = session.PlaylistItemId,
            PlaybackPositionTicks = position.Ticks,
            IsPaused = isPaused
        };
    }

    // Everything below is interface surface this plugin does not read. Each one
    // refuses rather than returning a default, so a test that starts depending
    // on it fails in the fake and names itself, instead of quietly agreeing
    // with whatever null or empty list happened to come back.
    private static Exception NotPartOfTheSurface([System.Runtime.CompilerServices.CallerMemberName] string member = "")
        => new NotSupportedException(
            member + " is not part of the server surface this plugin reads. Add it to FakeSessionManager only with the code that needs it.");

    /// <inheritdoc />
    public Task<SessionInfo> LogSessionActivity(string appName, string appVersion, string deviceId, string deviceName, string remoteEndPoint, User user)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void OnSessionControllerConnected(SessionInfo session) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void UpdateDeviceName(string sessionId, string reportedDeviceName) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task OnPlaybackStart(PlaybackStartInfo info) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task OnPlaybackProgress(PlaybackProgressInfo info) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task OnPlaybackProgress(PlaybackProgressInfo info, bool isAutomated) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task OnPlaybackStopped(PlaybackStopInfo info) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public ValueTask ReportSessionEnded(string sessionId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendGeneralCommand(string controllingSessionId, string sessionId, GeneralCommand command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendMessageCommand(string controllingSessionId, string sessionId, MessageCommand command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendPlayCommand(string controllingSessionId, string sessionId, PlayRequest command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendSyncPlayCommand(string sessionId, SendCommand command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendSyncPlayGroupUpdate<T>(string sessionId, GroupUpdate<T> command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendBrowseCommand(string controllingSessionId, string sessionId, BrowseRequest command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendPlaystateCommand(string controllingSessionId, string sessionId, PlaystateRequest command, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendMessageToAdminSessions<T>(SessionMessageType name, T data, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, T data, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendMessageToUserSessions<T>(List<Guid> userIds, SessionMessageType name, Func<T> dataFn, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendMessageToUserDeviceSessions<T>(string deviceId, SessionMessageType name, T data, CancellationToken cancellationToken)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task SendRestartRequiredNotification(CancellationToken cancellationToken) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void AddAdditionalUser(string sessionId, Guid userId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void RemoveAdditionalUser(string sessionId, Guid userId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void ReportNowViewingItem(string sessionId, string itemId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateNewSession(AuthenticationRequest request) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<AuthenticationResult> AuthenticateDirect(AuthenticationRequest request) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void ReportCapabilities(string sessionId, ClientCapabilities capabilities) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void ReportTranscodingInfo(string deviceId, TranscodingInfo info) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public void ClearTranscodingInfo(string deviceId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public SessionInfo GetSession(string deviceId, string client, string version) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public IReadOnlyList<SessionInfoDto> GetSessions(Guid userId, string deviceId, int? activeWithinSeconds, Guid? controllableUserToCheck, bool isApiKey)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<SessionInfo> GetSessionByAuthenticationToken(string token, string deviceId, string remoteEndpoint)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task<SessionInfo> GetSessionByAuthenticationToken(Device info, string deviceId, string remoteEndpoint, string appVersion)
        => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task Logout(string accessToken) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task Logout(Device device) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task RevokeUserTokens(Guid userId, string currentAccessToken) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task CloseIfNeededAsync(SessionInfo session) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public Task CloseLiveStreamIfNeededAsync(string liveStreamId, string sessionIdOrPlaySessionId) => throw NotPartOfTheSurface();

    /// <inheritdoc />
    public SessionInfoDto ToSessionInfoDto(SessionInfo sessionInfo) => throw NotPartOfTheSurface();
}
