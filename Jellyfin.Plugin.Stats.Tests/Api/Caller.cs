// The four callers every endpoint of this plugin is asked about, defined once.
//
// Issue #25 asks for exactly these four and asks that they be defined in one
// place, so that a new endpoint gets its authorization matrix by naming it
// rather than by somebody writing a fifth idea of what "an ordinary user"
// means. Everything here is a value: no server, no socket, no token, and no
// account that exists anywhere but in this file.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Net;

namespace Jellyfin.Plugin.Stats.Tests.Api;

/// <summary>
/// One of the four shapes a request to this plugin can arrive in.
/// </summary>
public sealed class Caller
{
    private Caller(string name, User? account, bool elevated)
    {
        Name = name;
        Account = account;
        IsAdministrator = elevated;
    }

    /// <summary>
    /// Gets a request that carries no authenticated caller at all.
    /// </summary>
    public static Caller Anonymous { get; } = new("anonymous", null, false);

    /// <summary>
    /// Gets an ordinary signed-in user.
    /// </summary>
    public static Caller Someone { get; } = new(
        "an ordinary user",
        FakeUserManager.NewUser("someone", new Guid("11111111-1111-1111-1111-111111111111")),
        false);

    /// <summary>
    /// Gets a second ordinary signed-in user, who is not the first one.
    /// </summary>
    /// <remarks>
    /// The shape that catches the mistake the first one cannot. An endpoint
    /// that ignores the account in the route and serves the caller theirs
    /// answers correctly for one ordinary user and wrongly for the other, and
    /// only a second account can tell those apart.
    /// </remarks>
    public static Caller SomeoneElse { get; } = new(
        "a different ordinary user",
        FakeUserManager.NewUser("someone else", new Guid("22222222-2222-2222-2222-222222222222")),
        false);

    /// <summary>
    /// Gets a signed-in administrator.
    /// </summary>
    /// <remarks>
    /// Carried as its own shape even though no endpoint in this plugin reads
    /// elevation today, because the row this shape produces is the statement
    /// issue #43 is about: an administrator asking for somebody else's detail
    /// is refused like anybody else. A matrix without this shape would be a
    /// matrix that never asked.
    /// </remarks>
    public static Caller Administrator { get; } = new(
        "an administrator",
        FakeUserManager.NewUser("administrator", new Guid("33333333-3333-3333-3333-333333333333")),
        true);

    /// <summary>
    /// Gets the four shapes, in the order the matrix reads them.
    /// </summary>
    public static IReadOnlyList<Caller> All { get; } = [Anonymous, Someone, SomeoneElse, Administrator];

    /// <summary>
    /// Gets what this caller is called in a table and in a failure message.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the account behind the request, and nothing where there is none.
    /// </summary>
    public User? Account { get; }

    /// <summary>
    /// Gets a value indicating whether this caller is an administrator.
    /// </summary>
    public bool IsAdministrator { get; }

    /// <summary>
    /// Gets this caller's account identifier, and the empty one where the
    /// request carries no account.
    /// </summary>
    public Guid UserId => Account?.Id ?? Guid.Empty;

    /// <summary>
    /// What the server would have put on the request after authenticating it,
    /// and nothing at all for a request it did not authenticate.
    /// </summary>
    /// <returns>The principal, or <c>null</c> where the caller is anonymous.</returns>
    public ClaimsPrincipal? Principal()
    {
        if (Account is null)
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, Account.Username),
            new(ClaimTypes.NameIdentifier, Account.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture))
        };

        if (IsAdministrator)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, InProcessEndpoints.SchemeName));
    }

    /// <summary>
    /// What the server's own authorization context would answer about this
    /// request.
    /// </summary>
    /// <returns>The caller, as the server describes one.</returns>
    public AuthorizationInfo AsTheServerDescribesIt()
        => new() { User = Account, IsAuthenticated = Account is not null };
}
