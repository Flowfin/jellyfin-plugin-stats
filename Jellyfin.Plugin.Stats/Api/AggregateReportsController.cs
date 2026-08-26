using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// The reports about the server rather than about the caller, served to an
/// administrator and to nobody else.
/// </summary>
/// <remarks>
/// The first endpoint here that is not about the account asking. Every other
/// route in this plugin serves the caller their own rows and refuses every
/// other account, an administrator included, and an aggregate is the first
/// answer that is not about a person at all.
/// <para>
/// WHO MAY ASK WAS DECIDED ON ISSUE #55 ON 2026-08-24, AND IT IS AN
/// ADMINISTRATOR ONLY. The other answer available was any signed-in caller, on
/// the ground that the answer names nobody. Least privilege took it: a
/// server-wide figure is not something an ordinary account has business
/// reading, it matches the line issue #41 draws against deanonymisation, and
/// opening these views to every signed-in account later is an additive step
/// while the reverse would be a withdrawal.
/// </para>
/// <para>
/// The elevation is read off the account the server describes rather than
/// asserted by a policy name. <see cref="CallerIdentity.IsAnAdministrator"/>
/// carries the argument for that and is the function whose deletion does not
/// compile.
/// </para>
/// <para>
/// NOTHING A CALLER SENDS DECIDES HOW THE STORE IS READ. Issue #55 is the whole
/// reason this shape is what it is: the two choices this action takes are
/// mapped through <see cref="ClosedSet{T}"/> and a value in neither set is
/// refused before the store is opened, the range is bounded by the plugin's own
/// caps rather than by anything on the request, and how many rows come back is
/// a constant here rather than a number a caller may raise.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Stats/Reports")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class AggregateReportsController : ControllerBase
{
    /// <summary>
    /// How many rows a top list in an answer may hold.
    /// </summary>
    /// <remarks>
    /// A constant and not a parameter, for the reason
    /// <see cref="YourYearController.TopListLength"/> is one and for a second
    /// reason that belongs to this route: a length a caller chooses is a caller
    /// deciding how much of the server's history one request returns, and a top
    /// list somebody may set to a thousand has stopped being a top list and
    /// become a dump of everything anybody watched.
    /// </remarks>
    public const int TopListLength = 10;

    private readonly AggregateQueries _reports;
    private readonly IAuthorizationContext _callers;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateReportsController"/> class.
    /// </summary>
    /// <param name="reports">The one route a report has to the plays.</param>
    /// <param name="callers">What the server says about who made a request.</param>
    public AggregateReportsController(AggregateQueries reports, IAuthorizationContext callers)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(callers);

        _reports = reports;
        _callers = callers;
    }

    /// <summary>
    /// Gets what a request may ask a top list to be grouped by.
    /// </summary>
    public static ClosedSet<TopListGrouping> Groupings { get; } = new(
        new KeyValuePair<string, TopListGrouping>("item", TopListGrouping.Item),
        new KeyValuePair<string, TopListGrouping>("series", TopListGrouping.Series));

    /// <summary>
    /// Gets what a request may ask a top list to be ordered by.
    /// </summary>
    public static ClosedSet<TopListOrder> Orders { get; } = new(
        new KeyValuePair<string, TopListOrder>("watchedTime", TopListOrder.WatchedTime),
        new KeyValuePair<string, TopListOrder>("plays", TopListOrder.Plays));

    /// <summary>
    /// Gets what a request may ask a breakdown to be grouped by.
    /// </summary>
    /// <remarks>
    /// The account is deliberately absent, and it is absent from the
    /// enumeration behind this rather than from the spellings here, so a
    /// request cannot ask for it whatever it writes.
    /// <see cref="PlayDimension"/> carries the argument.
    /// </remarks>
    public static ClosedSet<PlayDimension> Dimensions { get; } = new(
        new KeyValuePair<string, PlayDimension>("client", PlayDimension.Client),
        new KeyValuePair<string, PlayDimension>("device", PlayDimension.Device));

    /// <summary>
    /// Reads the most watched titles over a range, across the whole server.
    /// </summary>
    /// <remarks>
    /// Both ends of the range are required. On the deletion route an absent
    /// window means every play the account has, and that spelling is the one
    /// that costs; here an absent end is refused, so a request that lost half
    /// its query on the way cannot become a report over a period nobody asked
    /// for. That also removes the case issue #229 had to build a guard for: a
    /// named end carrying nothing binds to no instant, which is the same
    /// nothing an absent end binds to, and both are refused by the same line
    /// rather than being told apart.
    /// <para>
    /// The two choices are read before the store is opened. Reading them where
    /// they are used would make the refusal depend on the rows, so an unknown
    /// order would pass over a range holding one row and be refused over the
    /// next one, and a guard that fires on some requests and not others is not
    /// a guard.
    /// </para>
    /// <para>
    /// THE CHOICE IS CALLED <c>grouping</c> ON THE WIRE AND NOT <c>groupBy</c>,
    /// and the second name is refused rather than merely disliked.
    /// <c>no-query-from-the-request</c> in <c>tools/invariants/rules</c> fails
    /// the run on a request-bound parameter whose name is a query, a column, a
    /// sort or a grouping, and <c>groupBy</c> is one of the words it names. It
    /// cannot tell this parameter, which is compared against two spellings this
    /// build declares, from one that reaches a statement; that is the bound its
    /// own record states. So the answer is the name, not an exception: nothing
    /// here needs the SQL word, the layer under this calls the same thing a
    /// grouping, and a rule argued away once for a good reason is a rule the
    /// next parameter is argued away from for a worse one.
    /// </para>
    /// </remarks>
    /// <param name="from">The first moment of the range.</param>
    /// <param name="to">The first moment after the range.</param>
    /// <param name="grouping">Whether a row is an item or the series an episode belongs to. One of <see cref="Groupings"/>, and the item where the request does not say.</param>
    /// <param name="order">Which figure decides the order and therefore the cut. One of <see cref="Orders"/>, and watched time where the request does not say.</param>
    /// <returns>The top titles.</returns>
    /// <response code="200">The top titles, or the statement that the list is withheld.</response>
    /// <response code="400">The range is absent, unreadable or longer than this plugin answers over, or a choice was named that is not one this plugin knows.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than an empty one.</response>
    [HttpGet("Top")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TopTitles>> GetTopTitles(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? grouping,
        [FromQuery] string? order)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.IsAnAdministrator(caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!Chose(nameof(grouping), grouping, Groupings, TopListGrouping.Item, out var groupRowsBy)
            || !Chose(nameof(order), order, Orders, TopListOrder.WatchedTime, out var sort)
            || !ARangeThisPluginAnswersOver(from, to, out var window))
        {
            return BadRequest();
        }

        try
        {
            var rows = _reports.Top(window, TopListLength, groupRowsBy, sort);

            return Ok(rows is null ? TopTitles.NotShown : TopTitles.Of(rows));
        }
        catch (TooManyPlaysToAnswerException)
        {
            // The second cap, and the one that cannot be read off the request:
            // how long a range is, is known before anything is opened, and how
            // many plays it holds is not. The caller is told to ask over a
            // shorter range, which is what a refused request is. Which status a
            // bounded refusal ought to carry is issue #56's to settle if it
            // wants a different one; what this line will not do is answer with
            // the part of the range that fitted.
            return BadRequest();
        }
        catch (StoreCouldNotBeOpenedException)
        {
            // The plugin having no answer, which is a different fact from a
            // range in which nothing was watched. Narrow on purpose, for the
            // reason the same handler on the year route states: everything else
            // this action can throw is a defect in this plugin rather than a
            // state of the file, and a handler catching those too would report
            // every one of them as a store that is briefly away and be
            // believed.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Reads how a range divides between the clients or the devices the plays
    /// came from, across the whole server.
    /// </summary>
    /// <remarks>
    /// The question this answers is what asked the server to do the work, and
    /// that is a client and a device rather than a person. Nothing in the answer
    /// can name an account: the row type has no field for one and the set a
    /// request may group by has no account in it, so the response is the same
    /// whatever anybody has said about being named. Issue #59.
    /// <para>
    /// A member only one account used is not a row. It is folded into the group
    /// the answer carries beside the rows, and where what would fold stands on
    /// too few accounts to be shown at all the whole breakdown is withheld,
    /// which is a different answer from an empty one and is carried as one.
    /// Issue #41 is where that rule is.
    /// </para>
    /// </remarks>
    /// <param name="from">The first moment of the range.</param>
    /// <param name="to">The first moment after the range.</param>
    /// <param name="dimension">What to group by. One of <see cref="Dimensions"/>, and the client where the request does not say.</param>
    /// <returns>The breakdown.</returns>
    /// <response code="200">The rows and the group, or the statement that the breakdown is withheld.</response>
    /// <response code="400">The range is absent, unreadable or longer than this plugin answers over, or a dimension was named that is not one this plugin knows.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than an empty one.</response>
    [HttpGet("Breakdown")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BreakdownReport>> GetBreakdown(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? dimension)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.IsAnAdministrator(caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (!Chose(nameof(dimension), dimension, Dimensions, PlayDimension.Client, out var groupRowsBy)
            || !ARangeThisPluginAnswersOver(from, to, out var window))
        {
            return BadRequest();
        }

        try
        {
            var folded = _reports.Breakdown(window, groupRowsBy);

            return Ok(folded is null ? BreakdownReport.NotShown : BreakdownReport.Of(folded));
        }
        catch (TooManyPlaysToAnswerException)
        {
            return BadRequest();
        }
        catch (StoreCouldNotBeOpenedException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Reads the range off the request, where it is one this plugin will answer
    /// over.
    /// </summary>
    /// <remarks>
    /// Both ends are required. On the deletion route an absent window means
    /// every play the account has, and that spelling is the one that costs;
    /// here an absent end is refused, so a request that lost half its query on
    /// the way cannot become a report over a period nobody asked for. That also
    /// removes the case issue #229 had to build a guard for: a named end
    /// carrying nothing binds to no instant, which is the same nothing an absent
    /// end binds to, and both are refused by the same line rather than being
    /// told apart.
    /// <para>
    /// A range that ends before it starts, or one longer than any shape here
    /// answers over, is the caller asking for something this plugin will not do
    /// rather than the plugin failing. Both are refused rather than shortened,
    /// because a report folded from the part of a range that fitted reads
    /// exactly like one folded from the whole of it.
    /// </para>
    /// </remarks>
    /// <param name="from">The first moment of the range.</param>
    /// <param name="to">The first moment after the range.</param>
    /// <param name="window">The range and the bound.</param>
    /// <returns><c>true</c> where the request named a range this plugin answers over.</returns>
    private static bool ARangeThisPluginAnswersOver(DateTimeOffset? from, DateTimeOffset? to, out QueryWindow window)
    {
        window = null!;

        if (from is null || to is null)
        {
            return false;
        }

        try
        {
            window = QueryWindow.Of(from.Value.UtcDateTime, to.Value.UtcDateTime);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads one choice off the request, or the default where the request did
    /// not make it.
    /// </summary>
    /// <remarks>
    /// AN EMPTY VALUE IS NOT AN ABSENT ONE, and everything here is about
    /// keeping those two apart. Model binding turns an empty query value into
    /// no value at all, so a choice a caller named and left blank arrives at
    /// this action looking exactly like a choice they never mentioned. Left
    /// alone, that answers with the default member and calls it what the caller
    /// asked for. It is the shape issue #229 found on the deletion route, where
    /// the same erasure turned two empty date fields into every play the
    /// account had, and it is why the query is asked whether it carries the
    /// name rather than only what binding made of the value.
    /// <para>
    /// The query is read for a NAME and never for a value. What the choice
    /// means still comes from the set, so nothing a caller writes reaches
    /// anything but a spelling being compared against the ones this build
    /// declares.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">What the choice is.</typeparam>
    /// <param name="name">The parameter's name, as it appears in the query.</param>
    /// <param name="bound">What binding made of it.</param>
    /// <param name="set">The values the request may name.</param>
    /// <param name="unlessNamed">What the choice is where the request does not make it.</param>
    /// <param name="chosen">What was chosen.</param>
    /// <returns><c>true</c> where the request either made no choice or made one this plugin knows.</returns>
    private bool Chose<T>(string name, string? bound, ClosedSet<T> set, T unlessNamed, out T chosen)
        where T : struct, Enum
    {
        if (bound is null && !Request.Query.ContainsKey(name))
        {
            chosen = unlessNamed;
            return true;
        }

        return set.TryMap(bound, out chosen);
    }
}
