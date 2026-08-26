// What this plugin's one question about elevation reads, and what it refuses.
//
// The aggregate reports are served to an administrator alone, decided on issue
// #55 on 2026-08-24. The authorization matrix drives that through the endpoint
// with the four caller shapes; this drives the function under it, because the
// cases that matter most are the ones no caller shape stands for: a request
// with a token and no person behind it, and an account whose record says
// nothing about the permission at all.

using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Net;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// Who this plugin treats as an administrator.
/// </summary>
public class ElevationIsReadOffTheAccountTests
{
    /// <summary>
    /// An account carrying the permission is an administrator.
    /// </summary>
    [Fact]
    public void AnAccountCarryingThePermissionIsAnAdministrator()
        => Assert.True(CallerIdentity.IsAnAdministrator(Asking(WithTheAdministratorPermission(true))));

    /// <summary>
    /// An account whose record says the permission is withheld is not one.
    /// </summary>
    /// <remarks>
    /// The entry is present and says no. A check that asked whether the record
    /// mentions the permission rather than what it says would pass this and
    /// hand the server's figures to every account that has ever had one.
    /// </remarks>
    [Fact]
    public void AnAccountWhoseRecordWithholdsThePermissionIsNot()
        => Assert.False(CallerIdentity.IsAnAdministrator(Asking(WithTheAdministratorPermission(false))));

    /// <summary>
    /// An account whose record says nothing about the permission is not one.
    /// </summary>
    /// <remarks>
    /// The absent case, which is a different line from the withheld one and is
    /// the one that decides what happens to a record this plugin did not
    /// expect. Silence is not consent to read the server's figures.
    /// </remarks>
    [Fact]
    public void AnAccountWhoseRecordSaysNothingAboutItIsNot()
        => Assert.False(CallerIdentity.IsAnAdministrator(Asking(WithNoPermissionsAtAll())));

    /// <summary>
    /// An account carrying other permissions and not this one is not an
    /// administrator.
    /// </summary>
    /// <remarks>
    /// The record is walked to its end here rather than answered by its first
    /// entry, which is the case an account with a full default set of
    /// permissions is. A read that stopped at whatever entry came first would
    /// answer this account by whichever permission a collection happened to
    /// hold at the front.
    /// </remarks>
    [Fact]
    public void AnAccountCarryingOtherPermissionsAndNotThisOneIsNot()
    {
        var account = WithNoPermissionsAtAll();

        account.Permissions.Add(new Permission(PermissionKind.IsDisabled, false));
        account.Permissions.Add(new Permission(PermissionKind.EnableAllFolders, true));

        Assert.False(CallerIdentity.IsAnAdministrator(Asking(account)));
    }

    /// <summary>
    /// A request with no account behind it is not an administrator, however the
    /// server would have described it.
    /// </summary>
    /// <remarks>
    /// AN API KEY IS AN ADMINISTRATOR TO THE SERVER AND IS NOT ONE HERE. Read
    /// at <c>v10.11.11</c> and at <c>v12.0-rc5</c>, the server's own
    /// authentication handler gives the administrator role to any request whose
    /// token is a key, before it looks at an account at all, and such a request
    /// carries no account for this to read. That narrowing is deliberate and is
    /// argued at the function; this is the case that would go quiet if somebody
    /// widened it.
    /// </remarks>
    [Fact]
    public void ARequestWithNoAccountBehindItIsNot()
        => Assert.False(CallerIdentity.IsAnAdministrator(new AuthorizationInfo { User = null, IsAuthenticated = true }));

    /// <summary>
    /// Nothing at all is refused rather than treated as nobody.
    /// </summary>
    [Fact]
    public void NothingAtAllIsRefused()
        => Assert.Throws<ArgumentNullException>(() => CallerIdentity.IsAnAdministrator(null!));

    /// <summary>
    /// Of the four caller shapes the matrix is written over, exactly one is
    /// elevated, and it is elevated by the permission rather than by the flag
    /// beside it.
    /// </summary>
    /// <remarks>
    /// The shapes carry a boolean of their own, which is what the table reads
    /// when it decides which cell a row is about. Nothing made the two agree
    /// until this: a shape whose flag said administrator and whose account
    /// carried no permission would produce a matrix row that reads as an
    /// elevated caller being served and is a test of an ordinary one being
    /// refused, which is worse than no row at all.
    /// </remarks>
    [Fact]
    public void TheAdministratorShapeIsTheOnlyElevatedOne()
    {
        foreach (var shape in Caller.All)
        {
            Assert.Equal(shape.IsAdministrator, CallerIdentity.IsAnAdministrator(shape.AsTheServerDescribesIt()));
        }

        Assert.Single(Caller.All, shape => shape.IsAdministrator);
    }

    private static AuthorizationInfo Asking(User account)
        => new() { User = account, IsAuthenticated = true };

    private static User WithTheAdministratorPermission(bool value)
    {
        var account = WithNoPermissionsAtAll();

        account.Permissions.Add(new Permission(PermissionKind.IsAdministrator, value));

        return account;
    }

    private static User WithNoPermissionsAtAll()
    {
        var account = FakeUserManager.NewUser("somebody", Guid.Parse("44444444-4444-4444-4444-444444444444"));

        account.Permissions.Clear();

        return account;
    }
}
