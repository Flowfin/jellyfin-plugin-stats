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
using System.Diagnostics;
using System.IO;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Configuration;
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
    public InProcessEndpoints(
        Func<Guid, int, TimeZoneInfo, int, YearInReview>? fold = null,
        PluginConfiguration? configuration = null,
        TimeProvider? clock = null)
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
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
    public async Task<Answer> Get(string path, Caller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        _who.Is = caller;

        using var scope = _services.CreateScope();
        using var body = new MemoryStream();

        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

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
