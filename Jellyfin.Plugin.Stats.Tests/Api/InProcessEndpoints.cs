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
using Jellyfin.Plugin.Stats.Reports;
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
    /// <param name="consent">What holds each account's answer about being named. Defaults to one over a store that keeps answers in memory.</param>
    /// <param name="access">What the library says about which items a caller may see. Defaults to one where every item asked about is visible, so a test about anything else is not silently testing the access rule.</param>
    /// <param name="reports">The layer the aggregate routes answer through. Defaults to one over a store holding no plays, so a test about a status is not also a test about somebody's arithmetic.</param>
    /// <param name="held">What years an account has plays in. Defaults to none, for the reason the fold does: a case about a status is not a case about somebody's history.</param>
    public InProcessEndpoints(
        Func<Guid, int, TimeZoneInfo, int, YearInReview>? fold = null,
        PluginConfiguration? configuration = null,
        TimeProvider? clock = null,
        OwnHistoryDeletion? deletion = null,
        ConsentRegister? consent = null,
        IItemAccess? access = null,
        AggregateQueries? reports = null,
        YearsAnAccountHas? held = null)
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
        services.AddSingleton(held ?? NoYears);
        services.AddSingleton(access ?? FakeItemAccess.EverythingVisible);
        services.AddSingleton(deletion ?? new OwnHistoryDeletion(() => new NothingStored(), 1));

        // One store for the register rather than one per call, so an answer
        // recorded through the endpoint is there when the next request reads
        // it.
        var answers = new NothingStored();
        services.AddSingleton(consent ?? new ConsentRegister(() => answers, moment));
        services.AddSingleton(reports ?? new AggregateQueries(() => new NothingStored()));
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
    /// The years an account has where a case named none.
    /// </summary>
    /// <remarks>
    /// Empty rather than a year of its own, the same choice the fold above
    /// makes: a case about who may ask is not a case about what they watched,
    /// and a default that answered with a year would put one into every such
    /// case without anybody choosing it.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="zone">The zone the years would be read in.</param>
    /// <returns>No years.</returns>
    private static IReadOnlyList<int> NoYears(Guid userId, TimeZoneInfo zone) => [];

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
    /// <param name="body">The request body as JSON, where the request carries one.</param>
    /// <returns>What came back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
    public async Task<Answer> Send(string method, string path, Caller caller, string? body = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(path);

        _who.Is = caller;

        using var scope = _services.CreateScope();
        using var answered = new MemoryStream();

        var at = path.IndexOf('?', StringComparison.Ordinal);

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = at < 0 ? path : path[..at];

        if (at >= 0)
        {
            context.Request.QueryString = new QueryString(path[at..]);
        }

        if (body is not null)
        {
            var sent = System.Text.Encoding.UTF8.GetBytes(body);

            context.Request.ContentType = "application/json";
            context.Request.ContentLength = sent.Length;
            context.Request.Body = new MemoryStream(sent);
        }

        // The response body arrives through the feature rather than through the
        // property. Assigning the stream alone leaves the framework writing to
        // the pipe the feature holds, and a test then reads an empty buffer and
        // concludes the endpoint answered with nothing.
        context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(answered));

        await _pipeline(context).ConfigureAwait(false);
        await context.Response.CompleteAsync().ConfigureAwait(false);

        answered.Position = 0;
        using var reader = new StreamReader(answered);
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
    /// It answers the two deletions with nought, a range with no plays, and
    /// refuses everything else, so a test that reached this by accident fails
    /// rather than passing over an answer nobody arranged.
    /// </remarks>
    private sealed class NothingStored : IPlayStore
    {
        private readonly Dictionary<Guid, ConsentRecord> _consents = new();

        public ConsentRecord? ConsentFor(Guid userId)
            => _consents.TryGetValue(userId, out var consent) ? consent : null;

        public void RecordConsent(ConsentRecord consent) => _consents[consent.UserId] = consent;

        public void ForgetConsentFor(Guid userId) => _consents.Remove(userId);

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit) => 0;

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit) => 0;

        public void ReclaimFreedSpace()
        {
        }

        public void Dispose()
        {
        }

        public void Add(PlayRecord play) => throw NotPartOfThis();

        // The one read this store answers, because an aggregate route reads a
        // range and a matrix cell about who may ask has to reach the answer
        // rather than a failure. A range holding nothing is the honest empty
        // server, and a test about what an aggregate says is handed a layer of
        // its own rather than this one.
        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit) => [];

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        // A rollup this store never kept, and null is the honest answer rather
        // than a refusal. It used to throw, on the ground that answering with
        // none would let a caller that asked about days pass through a fake that
        // has none. The personal figures route made that reading wrong: a store
        // that has never keyed a rollup is a state it answers in full, from the
        // rows instead, so a throw here is a fake refusing a question the real
        // store answers. Issue #274.
        public TimeZoneInfo? RollupZone => null;

        public IEnumerable<DailyRollup> AllRollups() => throw NotPartOfThis();


        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithConsent() => throw NotPartOfThis();

        // Null is what a store holding no rows honestly holds, and it is the
        // second read an answer over a window takes - the server year takes it
        // as well as the personal figures do. It is admitted for the same reason
        // as the range above: a matrix cell about who may ask has to reach the
        // answer rather than a failure, and null is what makes that answer's
        // window say it covers no part of the year rather than claiming the
        // whole of it.
        public DateTime? OldestPlayStartedUtc() => null;

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => throw NotPartOfThis();

        public void RebuildRollups() => throw NotPartOfThis();

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
