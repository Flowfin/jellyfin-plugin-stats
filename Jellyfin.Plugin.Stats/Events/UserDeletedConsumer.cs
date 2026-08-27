using System;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Controller.Events;

namespace Jellyfin.Plugin.Stats.Events;

/// <summary>
/// Deletes a user's play rows when the server deletes the user.
/// </summary>
/// <remarks>
/// The user manager interface carries an update event and no deletion, so
/// nothing a plugin can subscribe to on that interface ever fires for this.
/// Deletion is published through the event consumer mechanism instead, and the
/// server resolves consumers out of a scope at the moment it publishes, which
/// is what lets a type registered by a plugin be called at all.
/// <para>
/// Everything this needs comes off the event argument. The server publishes
/// after it has saved, so by the time this runs the user is gone from the
/// server's own database and a consumer that took the identifier and then
/// asked the user manager for anything else would find nothing. That failure
/// would look like an empty deletion rather than an error, which is the worst
/// shape it could take.
/// </para>
/// <para>
/// The rows go a bite at a time, like the retention sweep's. A server one
/// person uses holds all of its rows under one identifier, so the set this
/// deletes is not small by construction, and one statement over years of rows
/// holds the store's write lock for its whole duration.
/// </para>
/// <para>
/// It throws rather than swallowing. The server catches what a consumer throws
/// and logs it, so a deletion that failed halfway is a line in the server's
/// log naming this type; a consumer that caught its own failure and returned
/// would leave rows behind with nothing anywhere saying so.
/// </para>
/// <para>
/// This names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name for the same reason the
/// retention sweep is. What that rule protects is the capture switch and the
/// per-user exclusion, which sit immediately before a write. Nothing here
/// writes a row.
/// </para>
/// </remarks>
public sealed class UserDeletedConsumer : IEventConsumer<UserDeletedEventArgs>
{
    /// <summary>
    /// How many rows one bite takes.
    /// </summary>
    /// <remarks>
    /// The retention sweep's number, and named here rather than taken from it
    /// because two callers reading one constant makes a change to either one a
    /// change to both.
    /// </remarks>
    public const int DefaultBite = 500;

    private readonly Func<IPlayStore> _openStore;
    private readonly int _bite;
    private readonly HeldYears? _heldYears;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserDeletedConsumer"/>
    /// class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per deletion, and what it returns is disposed of before the deletion returns.</param>
    /// <param name="bite">How many rows one statement deletes.</param>
    /// <param name="heldYears">
    /// What is keeping folded years, told to let this account's go. Null where
    /// nothing is keeping any, which is what a test driving the deletion alone
    /// hands in.
    /// </param>
    public UserDeletedConsumer(Func<IPlayStore> openStore, int bite, HeldYears? heldYears = null)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bite);

        _openStore = openStore;
        _bite = bite;
        _heldYears = heldYears;
    }

    /// <summary>
    /// Deletes every row belonging to the deleted user, and what that user
    /// said about being named, then reclaims the space they were using.
    /// </summary>
    /// <remarks>
    /// The reclaim is what makes this a deletion rather than a soft one. A
    /// delete leaves the row's bytes in a page the file has stopped pointing
    /// at, and a page that is no longer reachable through the schema is still
    /// in the file for anybody reading it, which is the thing an account
    /// deletion is being asked for. What it costs is a rewrite of the whole
    /// file, wanting room for a second copy, while the server waits for this
    /// to return.
    /// <para>
    /// It is skipped where nothing was deleted, because a rewrite of the file
    /// to reclaim no pages is that whole cost for no reason. A user with no
    /// rows is the ordinary case on a server where somebody was excluded from
    /// capture, or was added and removed without watching anything.
    /// </para>
    /// </remarks>
    /// <param name="eventArgs">The deletion, carrying the user it was.</param>
    /// <returns>A task that has already completed. Nothing here is asynchronous: SQLite is a file, and a task over it would be a thread pool thread waiting on a write lock.</returns>
    public Task OnEvent(UserDeletedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        var userId = eventArgs.Argument.Id;

        using var store = _openStore();

        // What that account said about being named goes with its rows. The
        // answer is a fact about a person, and an account the server no longer
        // has has nobody left to have answered; a record kept past the account
        // is a stored statement about somebody who is gone. Issue #44's first
        // condition names it beside the rows.
        //
        // Before the rows rather than after, so a deletion that fails part of
        // the way through the plays has already taken the smaller and more
        // personal of the two.
        store.ForgetConsentFor(userId);

        var deleted = 0;
        while (true)
        {
            // Corrective. The account is gone, so its plays stop being
            // counted rather than merely stopping being kept, and every figure
            // that counted them has to move. That is the difference from the
            // retention sweep, which removes rows of plays that still happened.
            var bitten = store.DeletePlaysFor(userId, DeletionClass.Corrective, _bite);
            if (bitten == 0)
            {
                break;
            }

            deleted += bitten;
        }

        if (deleted > 0)
        {
            store.ReclaimFreedSpace();
        }

        // Unconditionally, including where nothing was deleted. What this
        // account has just become is an account the server no longer has, so a
        // folded year kept under their identifier is a figure about somebody
        // who is gone, and the reclaim above has given back the pages the rows
        // it was folded from were sitting on. Letting it go here is what stops
        // it being the last remaining copy of what those rows said.
        _heldYears?.Forget(userId);

        return Task.CompletedTask;
    }
}
