using System;
using Jellyfin.Database.Implementations.Enums;
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

    /// <summary>
    /// Says whether the account that made a request is an administrator of this
    /// server.
    /// </summary>
    /// <remarks>
    /// The one question in this plugin that a permission answers, and it exists
    /// because the aggregate views are served to an administrator alone, which
    /// was decided on issue #55 on 2026-08-24. It says nothing about anybody's
    /// own rows: elevation widens nothing in
    /// <see cref="AsksForTheirOwnRows"/> and that function does not call this
    /// one.
    /// <para>
    /// THE PERMISSION IS READ OFF THE ACCOUNT RATHER THAN A POLICY NAME BEING
    /// ASSERTED, and the two fail differently. A policy is resolved by the
    /// host, so a plugin naming one has a refusal that is only as real as
    /// whatever registered that name, and a suite with no server under it has
    /// to define the policy itself and then assert against its own definition.
    /// The permission is a fact about the account, and a test can hand this
    /// function an account carrying it and an account that does not.
    /// </para>
    /// <para>
    /// It is the same permission the server derives its own answer from. Read
    /// at <c>v10.11.11</c> and at <c>v12.0-rc5</c>, in
    /// <c>Jellyfin.Api/Auth/CustomAuthenticationHandler.cs</c>, the role claim
    /// that <c>Policies.RequiresElevation</c> requires is set from
    /// <c>authorizationInfo.User?.HasPermission(PermissionKind.IsAdministrator)</c>
    /// on both lines. This walks the collection that method reads rather than
    /// calling it, because it is not on the surface this plugin references:
    /// the compiler answers CS1061 for <c>User.HasPermission</c> against
    /// <c>Jellyfin.Controller</c> and <c>Jellyfin.Model</c>, which are the two
    /// packages here, and adding a third package to shorten one loop is a
    /// dependency bought for a convenience.
    /// </para>
    /// <para>
    /// AN API KEY IS AN ADMINISTRATOR TO THE SERVER AND IS NOT ONE HERE, and
    /// that is a narrowing rather than an oversight. The same handler gives the
    /// administrator role to any request whose token is an API key, before it
    /// looks at an account at all, and such a request carries no account for
    /// this function to read. So a key that the server would let into every
    /// elevated route is refused these reports. That follows the line the
    /// function above already draws, where the empty identifier is refused
    /// rather than compared: what these views answer is about the people on a
    /// server, and the caller this plugin will show them to is a person who
    /// administers it.
    /// </para>
    /// </remarks>
    /// <param name="caller">Who the server says made the request.</param>
    /// <returns><c>true</c> where the request was made by an account holding the administrator permission.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="caller"/> is <c>null</c>.</exception>
    public static bool IsAnAdministrator(AuthorizationInfo caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (caller.User is not { } account)
        {
            return false;
        }

        foreach (var permission in account.Permissions)
        {
            if (permission.Kind == PermissionKind.IsAdministrator)
            {
                return permission.Value;
            }
        }

        return false;
    }
}
