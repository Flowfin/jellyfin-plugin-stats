using System;
using MediaBrowser.Controller.Net;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// Decides whether the rows an endpoint was asked for are the caller's own.
/// </summary>
/// <remarks>
/// A function of its own rather than a condition inside an action, because
/// issue #43 asks that removing the identity check fail a test rather than
/// change a response body. A condition written inline can be deleted and leave
/// a suite that still passes every status code assertion it had, since the
/// endpoint would then answer 200 to a request it used to refuse and only a
/// test that was already looking for the refusal would notice. Named here, the
/// check is a thing a test can call directly with the four caller shapes and a
/// thing whose deletion does not compile.
/// <para>
/// Consent is not read here and that is deliberate. It governs what other
/// people may see about somebody, and a person is not other people; issues #61
/// and #67 decided that, and #43 is where the sentence is anchored. A user
/// reads their own rows whether or not they have consented, whether or not they
/// have withdrawn, and this function has no opinion about it.
/// </para>
/// <para>
/// An administrator is refused by the same line as anybody else. The elevated
/// route to one person's history is the thing this plugin exists not to have,
/// and an administrator who needs numbers about the server has the aggregate
/// views, which name nobody without consent. There is no permission that widens
/// this, so there is no branch here that reads one.
/// </para>
/// </remarks>
public static class CallerIdentity
{
    /// <summary>
    /// Says whether an account named in a request is the account that made it.
    /// </summary>
    /// <remarks>
    /// The empty identifier is refused rather than compared. It is what
    /// <see cref="AuthorizationInfo.UserId"/> answers when the request carries
    /// no user at all, which is what an API key is, so a comparison alone would
    /// hand every row of the empty account to a caller that is not a person the
    /// moment a route ever produced that identifier.
    /// </remarks>
    /// <param name="asked">The account the request names.</param>
    /// <param name="caller">Who the server says made the request.</param>
    /// <returns><c>true</c> where the request names the account that made it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
    public static bool AsksForTheirOwnRows(Guid asked, AuthorizationInfo caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (asked.Equals(Guid.Empty))
        {
            return false;
        }

        return caller.UserId.Equals(asked);
    }
}
