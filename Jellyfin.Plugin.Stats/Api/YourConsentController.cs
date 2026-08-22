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
/// The route by which one account says whether it may be named, and reads back
/// what it said.
/// </summary>
/// <remarks>
/// Only the account itself. An administrator cannot set this for somebody and
/// cannot read what somebody said, which is the first condition of issue #42 and
/// is refused by the same line every other endpoint here refuses with. A consent
/// an administrator could record is not consent.
/// <para>
/// What agreeing changes is whether an administrator may see that account's
/// plays as theirs. It never changes whether the rows are kept, and the wording
/// the person is shown says so in its own words rather than leaving them to
/// infer it.
/// </para>
/// <para>
/// The wording travels with the answer. A page that fetched the two separately
/// could show one version's words beside another version's number, which is the
/// drift the stored version exists to catch.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("Stats/Users/{userId}/Consent")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class YourConsentController : ControllerBase
{
    private readonly ConsentRegister _consent;
    private readonly IAuthorizationContext _callers;

    /// <summary>
    /// Initializes a new instance of the <see cref="YourConsentController"/>
    /// class.
    /// </summary>
    /// <param name="consent">What holds the answers.</param>
    /// <param name="callers">What the server says about who made a request.</param>
    public YourConsentController(ConsentRegister consent, IAuthorizationContext callers)
    {
        ArgumentNullException.ThrowIfNull(consent);
        ArgumentNullException.ThrowIfNull(callers);

        _consent = consent;
        _callers = callers;
    }

    /// <summary>
    /// Reads what the calling account has said, and the wording it is about.
    /// </summary>
    /// <param name="userId">The account, which has to be the account asking.</param>
    /// <returns>What that account has said.</returns>
    /// <response code="200">The answer, and the wording it is about.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="503">The plugin could not open its store, so it has no answer rather than a negative one.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConsentState>> GetConsent([FromRoute] Guid userId)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            return Ok(AsAnAnswer(_consent.For(userId)));
        }
        catch (StoreCouldNotBeOpenedException)
        {
            // A store that will not open is the plugin having no answer, and an
            // answer saying nobody has agreed is an answer. Told apart here for
            // the reason the year endpoint gives.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Records what the calling account is saying now.
    /// </summary>
    /// <remarks>
    /// Agreeing names the version of the wording the person was shown, and a
    /// version other than the one this build ships is refused. A page that has
    /// gone stale behind an upgrade and a caller that made the number up are
    /// the same request from here, and both are answered by asking again rather
    /// than by recording an agreement to text nobody read.
    /// <para>
    /// Withdrawing names no version, because it is not an agreement to
    /// anything. It takes effect from the moment it is recorded, and the
    /// agreement it withdraws stays on the record beside it.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account, which has to be the account asking.</param>
    /// <param name="answer">What the account is saying.</param>
    /// <returns>What that account has now said.</returns>
    /// <response code="200">The answer as it now stands.</response>
    /// <response code="400">An agreement naming a version this build does not ship.</response>
    /// <response code="415">The request carried no body, so there was nothing to read it as.</response>
    /// <response code="401">The request carried no authenticated caller.</response>
    /// <response code="403">The request named an account other than the caller's.</response>
    /// <response code="503">The plugin could not open its store, so nothing was recorded.</response>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status415UnsupportedMediaType)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ConsentState>> SetConsent(
        [FromRoute] Guid userId,
        [FromBody] ConsentAnswer answer)
    {
        var caller = await _callers.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);

        if (!CallerIdentity.AsksForTheirOwnRows(userId, caller))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        // Nothing checks the body for being absent. The controller attribute
        // makes a body parameter required, so a request without one is refused
        // by the framework before this action is reached, and a check here
        // would be a branch no request could take and no proof could bite on.
        // The refusal is asserted where this endpoint is driven.
        try
        {
            if (!answer.Agreed)
            {
                return Ok(AsAnAnswer(_consent.Withdraw(userId)));
            }

            return Ok(AsAnAnswer(_consent.Agree(userId, answer.WordingVersion)));
        }
        catch (ArgumentOutOfRangeException)
        {
            // The version, and nothing else in this action throws it. What it
            // means is that the person agreed to words this build does not
            // ship, which is a question to put again rather than a failure.
            return BadRequest();
        }
        catch (StoreCouldNotBeOpenedException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    /// <summary>
    /// Turns a record, or the absence of one, into what the endpoint answers
    /// with.
    /// </summary>
    /// <param name="record">What the account has said, or null.</param>
    /// <returns>The answer.</returns>
    private static ConsentState AsAnAnswer(ConsentRecord? record)
    {
        return new ConsentState
        {
            Answered = record is not null,
            Agreed = record?.Agreed ?? false,
            AgreedUtc = record?.AgreedUtc,
            WithdrawnUtc = record?.WithdrawnUtc,
            AgreedToVersion = record?.WordingVersion ?? 0,
            CurrentVersion = ConsentWording.Version,
            Wording = ConsentWording.Text
        };
    }
}
