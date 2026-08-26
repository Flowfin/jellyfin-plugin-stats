// The seam between a report and the server's library, and the three answers it
// gives. Issue #54.
//
// The class under test decides what an absent item and an absent account mean
// and delegates the one question only a server can answer. That split is what
// these cases are written against: every decision this plugin takes is driven
// here, and the delegation is asserted to be a delegation rather than
// re-implemented.
//
// WHAT IS NOT COVERED HERE, and it is stated rather than left to be discovered.
// The server's own visibility arithmetic is not exercised by any case in this
// suite. It reads parental ratings, blocked tags and which libraries an account
// reaches, and calling it on an item built in a test raises a null reference on
// the 10.11 line, measured by writing that case and running it. What stands in
// its place is that the call is one expression with no decision in it, so what
// this suite cannot reach is a delegation and not a rule.

using System;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class ItemAccessThroughTheLibraryTests
{
    private static readonly Guid Somebody = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid AnItem = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000000a");

    /// <summary>
    /// An item the library still holds, for an account the server still holds:
    /// the answer is the server's, whichever way it goes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheServerDecidesWhereBothTheItemAndTheAccountAreThere(bool theServerSaysYes)
    {
        var item = PlaySessionBuilder.LiveChannel("A channel", AnItem);
        var account = FakeUserManager.NewUser("somebody", Somebody);

        var asked = 0;

        var access = new LibraryItemAccess(
            _ => item,
            _ => account,
            (askedAbout, forWhom) =>
            {
                asked++;
                Assert.Same(item, askedAbout);
                Assert.Same(account, forWhom);
                return theServerSaysYes;
            });

        Assert.Equal(theServerSaysYes, access.MaySee(Somebody, AnItem));
        Assert.Equal(1, asked);
    }

    /// <summary>
    /// An item the library no longer holds is not an item somebody may not see.
    /// It is an item there is no access question about, and the report names it
    /// out of the row. This is the answer the first condition of issue #54 rests
    /// on, and collapsing it into "not visible" would empty a report of every
    /// item anybody has ever deleted.
    /// </summary>
    [Fact]
    public void AnItemTheLibraryHasLetGoOfIsNeitherVisibleNorWithheld()
    {
        var access = new LibraryItemAccess(
            _ => null,
            _ => FakeUserManager.NewUser("somebody", Somebody),
            NeverAsked);

        Assert.Null(access.MaySee(Somebody, AnItem));
    }

    /// <summary>
    /// An empty identifier is answered without a lookup. No row a report groups
    /// on carries one, and a read of an empty identifier is one that can only
    /// fail.
    /// </summary>
    [Fact]
    public void AnEmptyIdentifierIsAnsweredWithoutAskingTheLibrary()
    {
        var lookups = 0;

        var access = new LibraryItemAccess(
            _ =>
            {
                lookups++;
                return null;
            },
            _ => FakeUserManager.NewUser("somebody", Somebody),
            NeverAsked);

        Assert.Null(access.MaySee(Somebody, Guid.Empty));
        Assert.Equal(0, lookups);
    }

    /// <summary>
    /// An account the server no longer holds sees nothing. A report is being
    /// answered for somebody, and where the server cannot say who that is, no
    /// item is named to them: the rows are still counted, and only the names are
    /// withheld.
    /// </summary>
    [Fact]
    public void AnAccountTheServerNoLongerHoldsSeesNothing()
    {
        var access = new LibraryItemAccess(
            _ => PlaySessionBuilder.LiveChannel("A channel", AnItem),
            _ => null,
            NeverAsked);

        Assert.False(access.MaySee(Somebody, AnItem));
    }

    /// <summary>
    /// None of the three is something a caller may leave out. A missing one
    /// would be a null reference at the moment a report is read rather than at
    /// the moment the plugin is assembled.
    /// </summary>
    [Fact]
    public void EveryRouteToTheServerIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new LibraryItemAccess(
            null!,
            _ => null,
            NeverAsked));

        Assert.Throws<ArgumentNullException>(() => new LibraryItemAccess(
            _ => null,
            null!,
            NeverAsked));

        Assert.Throws<ArgumentNullException>(() => new LibraryItemAccess(
            _ => null,
            _ => null,
            null!));
    }

    private static bool NeverAsked(BaseItem item, User account)
    {
        Assert.Fail("The server was asked about an item or an account this plugin had already decided about.");

        return false;
    }
}
