using System;
using System.Net.Mime;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// The route by which one account removes its own plays.
/// </summary>
/// <remarks>
/// Withdrawing consent stops other people seeing somebody's detail and leaves
/// every row where it was. This is the other request, and until it existed the
/// only way for a person to have their history removed was to ask an
/// administrator to delete their account, which takes everything else they have
/// with it. Issue #46.
/// <para>
/// The account is named in the route and checked against the account the server
/// says made the request, for the reason <see cref="YourYearController"/> gives:
/// a route carrying the identifier is a route a test can drive at somebody
/// else's rows and watch be refused, and an endpoint taking no identifier
/// cannot be shown to refuse anything.
/// </para>
/// <para>
/// An administrator is refused here by the same line as anybody else. There is
/// no elevated route to one person's history in this plugin, and a deletion is
/// no more theirs to ask for than a reading is.
/// </para>
/// <para>
/// It reaches no store. What it holds is <see cref="OwnHistoryDeletion"/>, which
/// is handed the open as a function where the plugin is assembled, so the store
/// is opened by that function and never here, and
/// <c>no-store-write-outside-the-write-path</c> has nothing to refuse in this
/// file.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Stats/Users/{userId}/Plays")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class YourHistoryController : ControllerBase
{
    private readonly OwnHistoryDeletion _deletion;
    private readonly IAuthorizationContext _callers;

    /// <summary>
    /// Initializes a new instance of the <see cref="YourHistoryController"/>
    /// class.
    /// </summary>
    /// <param name="deletion">What removes the rows.</param>
    /// <param name="callers">What the server says about who made a request.</param>
    public YourHistoryController(OwnHistoryDeletion deletion, IAuthorizationContext callers)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        ArgumentNullException.ThrowIfNull(callers);

        _deletion = deletion;
        _callers = callers;
    }

    /// <summary>
    /// Deletes the calling account's own plays, all of them or those that
    /// started inside a window.
    /// </summary>
    /// <remarks>
    /// The deletion is permanent and it happens while the request is being
    /// served. There is no undo, no second step and nothing kept anywhere for a
    /// change of mind, and a page offering this has to say so before it sends
    /// the request rather than afterwards.
    /// <para>
    /// The window is named as two instants, each carrying its own offset, and
    /// it is half open: a play starting exactly at the end stays. Instants
    /// rather than dates, because a date is not an interval until somebody says
    /// whose midnight is meant, and this plugin answers that from the setting
    /// and never from the request, which is what
    /// <c>no-time-offset-from-the-request</c> refuses the other spelling of.
    /// A caller who wants a calendar day deleted works out its two instants
    /// where the zone is known and sends those.
    /// </para>
    /// <para>
    /// Leaving both out is how a caller asks for everything, and that is the
    /// only spelling of it. A parameter that is named and carries nothing this
    /// endpoint can read as an instant is refused, because the alternative is
    /// reading it as the parameter having been left out, which on this route
    /// means the account's whole history.
    /// </para>
    /// <para>
    /// A store that cannot be opened leaves here as a status rather than as a
    /// success over nothing. An answer saying rows went, from a plugin that
    /// never reached the file, is the one wrong answer this endpoint must not
    /// give.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account whose plays are to go, which has to be the account asking.</param>
    /// <param name="from">The first moment of the window, or left out of the query for every play the account has.</param>
    /// <param name="to">The first moment after the window, or left out of the query for every play the account has.</param>
    /// <returns>How many rows went.</returns>
    /// <response code="200">The rows are gone, and the body says how many there were.</response>
    /// <response code="400">A window parameter was named and carried no instant this endpoint could read, one end of the window was named without the other, or the window ends at or before it begins.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="503">The plugin could not open its store, so nothing was deleted.</response>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaysDeleted>> DeleteMyPlays(
        [FromRoute] Guid userId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        if (NamedButUnreadable(nameof(from), from) || NamedButUnreadable(nameof(to), to))
        {
            return BadRequest();
        }

        if (from.HasValue != to.HasValue)
        {
            return BadRequest();
        }

        if (from.HasValue && from.Value >= to!.Value)
        {
            return BadRequest();
        }

        try
        {
            return Ok(new PlaysDeleted
            {
                Removed = _deletion.Delete(userId, from?.UtcDateTime, to?.UtcDateTime)
            });
        }
        catch (StoreCouldNotBeOpenedException)
        {
            // The one failure that is answered rather than thrown, for the
            // reason the year endpoint gives: a store that will not open is the
            // plugin having done nothing, and nothing and nought are different
            // answers. What is not said is why, because the reason names a file
            // this plugin was given and this response goes to whoever asked.
            //
            // Narrow on purpose. Everything else this action can throw is a
            // defect in the plugin rather than a state of the file, and a
            // handler that caught those too would report every one of them as a
            // store that is briefly unavailable and be believed.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Says whether the request named a window parameter and gave it a value
    /// this endpoint could not read as an instant.
    /// </summary>
    /// <remarks>
    /// Binding answers with an instant or with nothing, and nothing is two
    /// different facts: the caller left the parameter out, or the caller sent
    /// it empty. Only the first of those means every play the account has, and
    /// the two arrive at this action as the same <c>null</c>.
    /// <para>
    /// The difference is destructive here. A form with two empty date fields
    /// sends <c>?from=&amp;to=</c>, which reads as a window nobody named, and a
    /// window nobody named is the account's whole history. That is the guess
    /// the refusal below it already refuses to make for one missing end, made
    /// silently for both.
    /// </para>
    /// <para>
    /// A value that is present and is not an instant at all never reaches this:
    /// binding fails and the framework answers 400 before the action runs, so
    /// what is left to catch is the value that binds to nothing. Issue #55.
    /// </para>
    /// </remarks>
    /// <param name="name">The parameter's name, as it appears in the query.</param>
    /// <param name="bound">What binding made of it.</param>
    /// <returns><c>true</c> where the query carries the name and the action was handed no instant.</returns>
    private bool NamedButUnreadable(string name, DateTimeOffset? bound)
        => !bound.HasValue && Request.Query.ContainsKey(name);
}
