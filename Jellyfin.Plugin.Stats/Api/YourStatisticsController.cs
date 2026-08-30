using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// One account's own figures over one of three windows, served to that account
/// and to nobody else.
/// </summary>
/// <remarks>
/// The four reads the self page draws from, cut out of issue #61 because they
/// are Api and Aggregation work while that issue's scope is the page directory.
/// Issue #274.
/// <para>
/// AN ADMINISTRATOR IS REFUSED HERE LIKE EVERYBODY ELSE, and that is the
/// opposite rule from the aggregate route next to it. What that one answers
/// names nobody and is an operator's business; what this one answers is entirely
/// about one person, and per-user detail is that person's or, with their
/// recorded consent, a named row on a server-wide view. There is no third way to
/// reach it, and elevation is not one.
/// </para>
/// <para>
/// NOTHING A CALLER SENDS DECIDES HOW THE STORE IS READ. The window is one of
/// three names mapped through a <see cref="ClosedSet{T}"/>, and a value in
/// neither the set nor the route is refused before the store is opened. There is
/// no range on the request: a range would be the reader deciding how much of the
/// store one request reads, which is what issue #55 refuses across this plugin.
/// </para>
/// <para>
/// The zone is the one named in the settings and read while the request is
/// served, and the moment the window ends at comes from the clock this class was
/// given. A window built from an offset the request carried would be two
/// machines deciding what "the last thirty days" means;
/// <c>no-time-offset-from-the-request</c> refuses that shape and
/// <c>no-ambient-clock</c> refuses the other half of it.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Stats/Users/{userId}/Statistics")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class YourStatisticsController : ControllerBase
{
    /// <summary>
    /// How many rows the top list in an answer may hold.
    /// </summary>
    /// <remarks>
    /// A constant and not a parameter. A length a caller chooses is a caller
    /// deciding how much of their history one request returns, and a top list
    /// somebody may set to a thousand has stopped being a top list.
    /// </remarks>
    public const int TopListLength = 5;

    private readonly AggregateQueries _reports;
    private readonly IAuthorizationContext _callers;
    private readonly Func<PluginConfiguration> _configuration;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="YourStatisticsController"/>
    /// class.
    /// </summary>
    /// <param name="reports">The one route a report has to the plays.</param>
    /// <param name="callers">What the server says about who made a request.</param>
    /// <param name="configuration">The current settings, read at the moment one is needed rather than held.</param>
    /// <param name="clock">Says which moment the window ends at, so the window is not worked out from a machine setting.</param>
    public YourStatisticsController(
        AggregateQueries reports,
        IAuthorizationContext callers,
        Func<PluginConfiguration> configuration,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(callers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);

        _reports = reports;
        _callers = callers;
        _configuration = configuration;
        _clock = clock;
    }

    /// <summary>
    /// Gets the windows a request may ask for.
    /// </summary>
    /// <remarks>
    /// The names are the page's, so what a reader clicks and what the server
    /// folds are one vocabulary rather than two that can drift. The set is
    /// exported so a case can read it back rather than repeating it.
    /// </remarks>
    public static ClosedSet<PersonalWindow> Windows { get; } = new(
        new KeyValuePair<string, PersonalWindow>("last30Days", PersonalWindow.Last30Days),
        new KeyValuePair<string, PersonalWindow>("last12Months", PersonalWindow.Last12Months),
        new KeyValuePair<string, PersonalWindow>("allTime", PersonalWindow.AllTime));

    /// <summary>
    /// Reads the calling account's own figures over one window.
    /// </summary>
    /// <remarks>
    /// A figure this plugin could not compute is absent from the answer with its
    /// reason beside it in <c>degraded</c>, and never nought. A nought is a
    /// person who watched nothing.
    /// </remarks>
    /// <param name="userId">The account whose figures are wanted, which has to be the account asking.</param>
    /// <param name="window">Which of the three windows.</param>
    /// <returns>The figures.</returns>
    /// <response code="200">The caller's own figures over the window.</response>
    /// <response code="400">The window is not one this plugin folds.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than an empty one.</response>
    [HttpGet("{window}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OwnFigures>> GetStatistics(
        [FromRoute] Guid userId,
        [FromRoute] string window)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // Read before the store is opened, so a window this build has no name
        // for is refused whatever the store holds rather than on some stores and
        // not others.
        if (!Windows.TryMap(window, out var chosen))
        {
            return BadRequest();
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);

        try
        {
            return Ok(_reports.FiguresFor(userId, chosen, zone, _clock.GetUtcNow(), TopListLength));
        }
        catch (StoreCouldNotBeOpenedException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
