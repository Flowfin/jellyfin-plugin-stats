using System;
using System.Globalization;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// One account's calendar year, served to that account and to nobody else.
/// </summary>
/// <remarks>
/// The first endpoint in this plugin. What it serves is the fold that was
/// already in the tree with nothing calling it, so what arrives here is the
/// route from the plugin to a page rather than a second copy of the arithmetic.
/// <para>
/// The account is named in the route and is checked against the account the
/// server says made the request, rather than being read off the request and
/// trusted. Both shapes serve the caller their own rows on a correct request;
/// they differ on a wrong one, and the difference is that a route carrying the
/// identifier is a route a test can drive at somebody else's rows and watch be
/// refused. An endpoint that takes no identifier cannot be shown to refuse
/// anything, because there is nothing to ask it for.
/// </para>
/// <para>
/// It reaches no store. The reports in this plan read through a layer rather
/// than through the store's interface, which is issue #51, and
/// <c>no-store-write-outside-the-write-path</c> refuses that interface being
/// named outside the write path at all. What this holds instead is
/// <see cref="HeldYears"/>, which is handed the fold as a function where the
/// plugin is assembled, so the store is opened by that function and never here.
/// </para>
/// <para>
/// It does reach the library, for one question and while the request is being
/// served: whether the account asking may still see an item a top list would
/// name. That is the only thing the library is permitted to answer here, and it
/// never supplies a label. Issue #54.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Stats/Users/{userId}/Years")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class YourYearController : ControllerBase
{
    /// <summary>
    /// How many rows a top list in an answer may hold.
    /// </summary>
    /// <remarks>
    /// A constant rather than a setting, for the reason the minimum group size
    /// on issue #41 is one: a number an installation can turn up is a number
    /// every test has to pin before it can assert anything, and a list length
    /// somebody can raise to a thousand is a top list that has stopped being
    /// one. It is not the response bound in the configuration either, which
    /// bounds how many rows a response may carry rather than how long a top
    /// list is, and reading a bound as a length is how a top ten becomes a
    /// dump of everything the account ever watched.
    /// </remarks>
    public const int TopListLength = 10;

    /// <summary>
    /// The oldest year this endpoint will answer for.
    /// </summary>
    /// <remarks>
    /// A bound rather than a claim about the rows. A held answer is kept per
    /// year asked for, so a year taken from the request with nothing in front
    /// of it is an unbounded number of held answers for one account, each one
    /// folded by walking that account's rows. What the floor is worth is
    /// stated rather than dressed up: it bounds the count and it is not the
    /// bound this plugin eventually wants, which is the set of years the store
    /// actually holds rows for. That read exists on the store already and
    /// reaching it from here needs a seam this change does not build. Issue #56
    /// is where every query being bounded is argued, and it is where a tighter
    /// bound belongs.
    /// </remarks>
    public const int EarliestYearAnswered = 1970;

    private readonly HeldYears _years;
    private readonly YearsAnAccountHas _held;
    private readonly IItemAccess _access;
    private readonly IAuthorizationContext _callers;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="YourYearController"/> class.
    /// </summary>
    /// <param name="years">Where a folded year is asked for.</param>
    /// <param name="held">Reads which years an account has plays in, which is what the selector may offer.</param>
    /// <param name="access">What the library says about which items the caller may still see, asked while the request is served.</param>
    /// <param name="callers">What the server says about who made a request.</param>
    /// <param name="configuration">The current settings, read at the moment one is needed rather than held.</param>
    /// <param name="clock">Says which year the server is in, so a year that has not happened is refused rather than folded.</param>
    public YourYearController(
        HeldYears years,
        YearsAnAccountHas held,
        IItemAccess access,
        IAuthorizationContext callers,
        Func<PluginConfiguration> configuration,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(years);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

        _years = years;
        _held = held;
        _access = access;
        _callers = callers;
        _configuration = configuration;
        _clock = clock;
    }

    /// <summary>
    /// Reads which years the calling account has plays in, and the day this
    /// plugin still keeps rows from.
    /// </summary>
    /// <remarks>
    /// The third condition of issue #67. A selector drawn from anything else
    /// offers years the store holds nothing of this account's in: the list a
    /// reader would otherwise reach for runs from the oldest row to the year the
    /// server is in, and a quiet year in the middle of that span opens empty.
    /// <para>
    /// The day rows are kept from travels with the list because the two answer
    /// one question between them. Inside the kept window a missing year is a
    /// year the account recorded nothing in; before it, no year could be offered
    /// whatever was recorded in one. A selector holding only the list cannot
    /// tell a reader which of the two a gap is, and a page that worked the day
    /// out from a setting and its own clock would be reading two machines where
    /// the rows have one.
    /// </para>
    /// <para>
    /// Nothing here says a swept year held anything. Whether this account
    /// watched something in a year whose rows are gone is not a question this
    /// tree can answer, and an answer that implied one would invent the history
    /// it is apologising for.
    /// </para>
    /// <para>
    /// It carries no bound, and the reason is the one the store's own read
    /// carries: the answer is one number per year the account has watched
    /// anything in, so it grows with how long they have been on the server and
    /// not with how much they watched. A million rows over three years answer
    /// with three numbers.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account whose years are wanted, which has to be the account asking.</param>
    /// <returns>The years, and the day rows are kept from.</returns>
    /// <response code="200">The caller's own years.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than an empty one.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<YearsHeld>> GetYears([FromRoute] Guid userId)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var settings = _configuration();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.RollupTimeZone);

        try
        {
            // The day is read from the same settings and the same clock the
            // sweep measures its cutoff back from, so what a page is told the
            // window is and what the sweep will delete next are one number
            // rather than two that drift apart by whatever a page assumed.
            var keptFrom = LocalDay.Of(
                _clock.GetUtcNow().AddDays(-settings.PlayRowRetentionDays),
                zone);

            return Ok(new YearsHeld(
                _held(userId, zone),
                keptFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }
        catch (StoreCouldNotBeOpenedException)
        {
            // The same translation the year below makes, and for the same
            // reason: an account with no years and a plugin that cannot read
            // any are different facts, and a selector handed an empty list for
            // the second would tell somebody they have watched nothing ever.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Reads one calendar year of the calling account's own plays.
    /// </summary>
    /// <remarks>
    /// The refusal is a status and not an empty year. A year the caller may not
    /// see and a year in which they watched nothing are different facts, and an
    /// endpoint answering both with the same body has destroyed the difference
    /// before anything can draw it.
    /// <para>
    /// A store that cannot be opened is a third fact and leaves here as a third
    /// status. It is the half of issue #31 the settings page does not cover: an
    /// operator reads the reason on that page, and a caller reads that the
    /// plugin is unavailable rather than that they watched nothing all year.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account whose year is wanted, which has to be the account asking.</param>
    /// <param name="year">The calendar year, read in the zone the settings name.</param>
    /// <returns>The year.</returns>
    /// <response code="200">The caller's own year.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="404">The year is outside what this endpoint answers for.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than an empty one.</response>
    [HttpGet("{year:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<YearInReview>> GetYear([FromRoute] Guid userId, [FromRoute] int year)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);

        if (year < EarliestYearAnswered || year > LocalDay.Of(_clock.GetUtcNow(), zone).Year)
        {
            return NotFound();
        }

        try
        {
            // The held answer is read for whoever is asking rather than served
            // as it was folded. A finished year is kept until the rows under
            // it move, and access is a fact about now: an item this account
            // lost sight of yesterday moved no row, so nothing would ever tell
            // the hold to let go of a list still naming it. Issue #54.
            return Ok(_years.For(userId, year, zone, TopListLength).SeenBy(userId, _access));
        }
        catch (StoreCouldNotBeOpenedException)
        {
            // The one failure that is answered rather than thrown. A store that
            // will not open is the plugin having no answer, and an empty year
            // is an answer, so the two leave here as different statuses. What
            // is not said is why: the reason names a file this plugin was given
            // and this response goes to whoever asked, so the operator reads it
            // on the settings page and the caller reads that there is nothing
            // to be had right now.
            //
            // Narrow on purpose. Everything else this action can throw is a
            // defect in the plugin rather than a state of the file, and a
            // handler that caught those too would report every one of them as
            // a store that is briefly unavailable and be believed.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
