// The route from a test to this plugin's endpoints, with no server under it.
//
// Issue #25 asks for an in-process host that routes requests to the plugin's
// controllers over an in-memory transport, with the server's authorization
// faked, and asks that nothing here bind a port or read a certificate. What
// stands in for the transport is a request object handed to a pipeline, and
// that is the whole of what a socket would have carried: the routing, the
// authorization filters, the model binding and the result execution are the
// real ones out of the framework the server runs on.
//
// Nothing in this file opens anything. There is no host, no listener and no
// address, so the rule refusing a bound port has nothing to refuse here rather
// than being worked around.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jellyfin.Plugin.Stats.Tests.Api;

/// <summary>
/// A pipeline that answers requests to this plugin's controllers in process.
/// </summary>
public sealed class InProcessEndpoints : IDisposable
{
    /// <summary>
    /// What the faked authentication scheme is called.
    /// </summary>
    /// <remarks>
    /// It stands where the server's own scheme stands. The plugin's endpoints
    /// name no scheme of their own, so what they get is whichever one is the
    /// default, and that is what makes this substitution honest: a request
    /// authenticated by this scheme reaches an action for the same reason one
    /// authenticated by the server's would.
    /// </remarks>
    public const string SchemeName = "InProcess";

    private readonly ServiceProvider _services;
    private readonly RequestDelegate _pipeline;
    private readonly CallerOfTheMoment _who = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InProcessEndpoints"/> class.
    /// </summary>
    /// <param name="fold">Folds one account's year. A test that reaches a year endpoint decides here what the store would have said.</param>
    /// <param name="configuration">The settings the endpoints read, or the defaults where none is given.</param>
    /// <param name="clock">The clock the endpoints read the current year from, or a fixed one in 2026 where none is given.</param>
    /// <param name="deletion">What removes an account's own plays. A test that only reads statuses lets this default to one over a store holding nothing.</param>
    public InProcessEndpoints(
        Func<Guid, int, TimeZoneInfo, int, YearInReview>? fold = null,
        PluginConfiguration? configuration = null,
        TimeProvider? clock = null,
        OwnHistoryDeletion? deletion = null)
    {
        var settings = configuration ?? new PluginConfiguration();
        var moment = clock ?? new FixedClock(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddLogging();

        // The one service the framework's controller route needs that neither
        // AddControllers nor AddRouting registers. Outside a web host nothing
        // else puts one in the container, and its absence fails while the
        // routes are being built rather than while one is being served, a long
        // way from the line that would have to change.
        services.AddSingleton(new DiagnosticListener("Jellyfin.Plugin.Stats.Tests"));

        services.AddRouting();
        services.AddAuthentication(SchemeName)
            .AddScheme<AuthenticationSchemeOptions, CallerScheme>(SchemeName, null);
        services.AddAuthorization();
        services.AddControllers().AddApplicationPart(typeof(YourYearController).Assembly);

        services.AddSingleton(_who);
        services.AddSingleton<IAuthorizationContext>(new CallerContext(_who));
        services.AddSingleton(new HeldYears(fold ?? NothingWatched, moment));
        services.AddSingleton(deletion ?? new OwnHistoryDeletion(() => new NothingStored(), 1));
        services.AddSingleton<Func<PluginConfiguration>>(() => settings);
        services.AddSingleton(moment);

        _services = services.BuildServiceProvider();

        var application = new ApplicationBuilder(_services);
        application.UseRouting();
        application.UseAuthentication();
        application.UseAuthorization();
        application.UseEndpoints(endpoints => endpoints.MapControllers());
        _pipeline = application.Build();
    }

    /// <summary>
    /// Sends one GET request as one of the four callers.
    /// </summary>
    /// <param name="path">The path, as it would appear after the host.</param>
    /// <param name="caller">Who is asking.</param>
    /// <returns>What came back.</returns>
    public Task<Answer> Get(string path, Caller caller) => Send(HttpMethods.Get, path, caller);

    /// <summary>
    /// Sends one request as one of the four callers.
    /// </summary>
    /// <remarks>
    /// The method is an argument rather than one wrapper per verb, because the
    /// authorization matrix carries the method in each of its rows and a
    /// harness that only sent one would answer every row over the same verb.
    /// A row about a deletion would then be proved by whatever the same path
    /// answers a read with, which on a route that has no read is a 405 in every
    /// cell and reads as a refusal.
    /// </remarks>
    /// <param name="method">The request method.</param>
    /// <param name="path">The path, as it would appear after the host, with its query if it has one.</param>
    /// <param name="caller">Who is asking.</param>
    /// <returns>What came back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
    public async Task<Answer> Send(string method, string path, Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(path);

        _who.Is = caller;

        using var scope = _services.CreateScope();
        using var body = new MemoryStream();

        var at = path.IndexOf('?', StringComparison.Ordinal);

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = at < 0 ? path : path[..at];

        if (at >= 0)
        {
            context.Request.QueryString = new QueryString(path[at..]);
        }

        // The response body arrives through the feature rather than through the
        // property. Assigning the stream alone leaves the framework writing to
        // the pipe the feature holds, and a test then reads an empty buffer and
        // concludes the endpoint answered with nothing.
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));

        await _pipeline(context).ConfigureAwait(false);
        await context.Response.CompleteAsync().ConfigureAwait(false);

        body.Position = 0;
        using var reader = new StreamReader(body);
        var text = await reader.ReadToEndAsync().ConfigureAwait(false);

        return new Answer(context.Response.StatusCode, text);
    }

    /// <inheritdoc />
    public void Dispose() => _services.Dispose();

    private static YearInReview NothingWatched(Guid userId, int year, TimeZoneInfo zone, int topCount)
        => YearInReview.Over([], userId, year, zone, topCount, null);

    /// <summary>
    /// A store with nothing in it, for the tests that only read statuses.
    /// </summary>
    /// <remarks>
    /// It answers the two deletions with nought and refuses everything else,
    /// so a test that reached this by accident fails rather than passing over
    /// an answer nobody arranged.
    /// </remarks>
    private sealed class NothingStored : IPlayStore
    {
        public int DeletePlaysFor(Guid userId, int limit) => 0;

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit) => 0;

        public void ReclaimFreedSpace()
        {
        }

        public void Dispose()
        {
        }

        public void Add(PlayRecord play) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit) => throw NotPartOfThis();

        public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

        public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

        public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("This store stands in for one with nothing in it, and answers only what a deletion asks.");
    }

    /// <summary>
    /// What one request came back with.
    /// </summary>
    /// <param name="Status">The status code.</param>
    /// <param name="Body">The body, as text.</param>
    public sealed record Answer(int Status, string Body);

    private sealed class CallerOfTheMoment
    {
        public Caller Is { get; set; } = Caller.Anonymous;
    }

    private sealed class CallerContext : IAuthorizationContext
    {
        private readonly CallerOfTheMoment _who;

        public CallerContext(CallerOfTheMoment who) => _who = who;

        public Task<AuthorizationInfo> GetAuthorizationInfo(HttpContext requestContext)
            => Task.FromResult(_who.Is.AsTheServerDescribesIt());

        public Task<AuthorizationInfo> GetAuthorizationInfo(HttpRequest requestContext)
            => Task.FromResult(_who.Is.AsTheServerDescribesIt());
    }

    private sealed class CallerScheme : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly CallerOfTheMoment _who;

        public CallerScheme(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            CallerOfTheMoment who)
            : base(options, logger, encoder)
        {
            _who = who;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsPrincipal? principal = _who.Is.Principal();

            return Task.FromResult(principal is null
                ? AuthenticateResult.NoResult()
                : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
        }
    }
}
