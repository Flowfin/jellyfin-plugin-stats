using System;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Answers the access question through the server's library and its accounts.
/// </summary>
/// <remarks>
/// The whole of this plugin's use of the library on the read path, and it asks
/// one thing. What arrives here are functions rather than the managers, which
/// holds the reach to its smallest surface the way
/// <see cref="Capture.LibraryChannelNames"/> does at the write path: this file
/// names the operations that are used rather than two interfaces carrying a
/// hundred between them, and a suite with no server can drive the decisions
/// below.
/// <para>
/// The last of the three functions is the one call this class cannot make
/// itself. Whether an account may see an item is the server's own arithmetic
/// over parental ratings, blocked tags and which libraries that account
/// reaches, and asking it from a suite with no server raises a null reference
/// on the older of the two supported lines. So it arrives as a function with no
/// decision in it, the three answers below are decided here where they can be
/// driven, and what is left unproven by any suite is one delegation rather than
/// a rule.
/// </para>
/// <para>
/// An account the server no longer holds sees nothing. That is the safe
/// direction and it is also the honest one: a report is being answered for
/// somebody, and where the server cannot say who that is, no item is named to
/// them. The rows are still counted, because a total says how much was watched
/// and not by whom.
/// </para>
/// <para>
/// Nothing is cached here. Access is a fact about now, and a remembered answer
/// is the fact as it was when the answer was remembered; the caller bounds how
/// often this is asked by stopping once it has the rows it needs, which is
/// where that cost belongs. Issue #54.
/// </para>
/// </remarks>
public sealed class LibraryItemAccess : IItemAccess
{
    private readonly Func<Guid, BaseItem?> _itemInTheLibrary;
    private readonly Func<Guid, User?> _accountOnTheServer;
    private readonly Func<BaseItem, User, bool> _visibleToTheAccount;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemAccess"/> class.
    /// </summary>
    /// <param name="itemInTheLibrary">How an identifier becomes the item the library holds for it, or null where it holds none.</param>
    /// <param name="accountOnTheServer">How an identifier becomes the account the server holds for it, or null where it holds none.</param>
    /// <param name="visibleToTheAccount">Whether the server would show that item to that account. It takes both and decides nothing else.</param>
    public LibraryItemAccess(
        Func<Guid, BaseItem?> itemInTheLibrary,
        Func<Guid, User?> accountOnTheServer,
        Func<BaseItem, User, bool> visibleToTheAccount)
    {
        _itemInTheLibrary = itemInTheLibrary ?? throw new ArgumentNullException(nameof(itemInTheLibrary));
        _accountOnTheServer = accountOnTheServer ?? throw new ArgumentNullException(nameof(accountOnTheServer));
        _visibleToTheAccount = visibleToTheAccount ?? throw new ArgumentNullException(nameof(visibleToTheAccount));
    }

    /// <inheritdoc />
    public bool? MaySee(Guid userId, Guid itemId)
    {
        // Nothing is asked for an identifier no row carries. A lookup of an
        // empty identifier is a read that can only fail, and the answer for it
        // is the same as for an item the library has let go of.
        if (itemId == Guid.Empty)
        {
            return null;
        }

        var item = _itemInTheLibrary(itemId);

        if (item is null)
        {
            return null;
        }

        var account = _accountOnTheServer(userId);

        if (account is null)
        {
            return false;
        }

        return _visibleToTheAccount(item, account);
    }
}
