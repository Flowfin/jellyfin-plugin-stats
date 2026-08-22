using System;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Privacy;

/// <summary>
/// Removes the plays of one account, at that account's own asking.
/// </summary>
/// <remarks>
/// Withdrawing consent stops other people seeing somebody's detail and leaves
/// every row where it was. This is the other request, and it is the one an
/// account makes about itself: the rows go, and they go from the file rather
/// than from what a reader is shown. Issue #46.
/// <para>
/// It is a type of its own rather than a body inside the endpoint, for the
/// reason <see cref="Api.CallerIdentity"/> is one: an endpoint that held the
/// deletion inline could have the identity check deleted from around it and go
/// on answering, and a suite reading status codes would not notice. Here the
/// endpoint decides who may ask and this decides what happens, and neither
/// half can be removed without the other failing to compile.
/// </para>
/// <para>
/// The shape is the account deletion's, deliberately. A user removing their own
/// history and a server removing a user's history are the same act asked for by
/// different people, so they take bites of the same size, reclaim the same way,
/// and let go of the same folded years. Answering either of them differently
/// would be a difference nobody chose.
/// </para>
/// <para>
/// This names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name for the reason the account
/// deletion and the two sweeps are. What that rule protects is the capture
/// switch and the per-user exclusion, which sit immediately before a write.
/// Nothing here writes a row.
/// </para>
/// </remarks>
public sealed class OwnHistoryDeletion
{
    /// <summary>
    /// How many rows one bite takes.
    /// </summary>
    /// <remarks>
    /// The account deletion's number, and named here rather than taken from it
    /// because two callers reading one constant makes a change to either one a
    /// change to both.
    /// </remarks>
    public const int DefaultBite = 500;

    private readonly Func<IPlayStore> _openStore;
    private readonly int _bite;
    private readonly HeldYears? _heldYears;

    /// <summary>
    /// Initializes a new instance of the <see cref="OwnHistoryDeletion"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per deletion, and what it returns is disposed of before the deletion returns.</param>
    /// <param name="bite">How many rows one statement deletes.</param>
    /// <param name="heldYears">
    /// What is keeping folded years, told to let this account's go. Null where
    /// nothing is keeping any, which is what a test driving the deletion alone
    /// hands in.
    /// </param>
    public OwnHistoryDeletion(Func<IPlayStore> openStore, int bite, HeldYears? heldYears = null)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bite);

        _openStore = openStore;
        _bite = bite;
        _heldYears = heldYears;
    }

    /// <summary>
    /// Deletes the account's own plays, all of them or those that started
    /// inside a window, and gives the space back.
    /// </summary>
    /// <remarks>
    /// The two bounds are both there or both absent. A half-named window is a
    /// caller who meant one thing and typed another, and guessing which end
    /// they left out is a guess about somebody's history.
    /// <para>
    /// The folded years are let go whatever the window was, and that is the
    /// half most easily left out. A held year is a detail view answered without
    /// reading a row, so a deletion that took the rows and left the hold would
    /// hand the caller their own year back afterwards, complete and looking
    /// correct, with nothing in it drawn from anything that still exists.
    /// <see cref="HeldYears.Forget(Guid)"/> drops every year held for the
    /// account rather than the years the window touched, so somebody deleting
    /// a fortnight loses every held year of their own and gets the next one
    /// folded again. Dropping more than was deleted is the safe direction, and
    /// it is what the account deletion already does.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account asking, which is the only account this ever reads or writes.</param>
    /// <param name="fromUtc">The first moment of the window, in UTC, or null for every play the account has.</param>
    /// <param name="toUtc">The first moment after the window, in UTC, or null for every play the account has.</param>
    /// <returns>How many rows went.</returns>
    /// <exception cref="ArgumentException">One bound was given and the other was not.</exception>
    public int Delete(Guid userId, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue != toUtc.HasValue)
        {
            throw new ArgumentException(
                "A window is named by both of its ends or by neither. One end alone does not say which rows were meant.",
                fromUtc.HasValue ? nameof(toUtc) : nameof(fromUtc));
        }

        // Through the same opener the year endpoint reads through, so a store
        // that will not open arrives at the endpoint above as the one failure
        // it answers with a status rather than as an answer saying nought rows
        // went. Writing the translation a second time here would be a second
        // answer to one question.
        var deleted = ReadFromTheStore.Answering(_openStore, store =>
        {
            var gone = 0;
            while (true)
            {
                var bitten = fromUtc.HasValue
                    ? store.DeletePlaysFor(userId, fromUtc.Value, toUtc!.Value, _bite)
                    : store.DeletePlaysFor(userId, _bite);

                if (bitten == 0)
                {
                    break;
                }

                gone += bitten;
            }

            if (gone > 0)
            {
                // What makes this a deletion rather than a soft one. A delete
                // leaves the row's bytes in a page the file has stopped
                // pointing at, and a page nothing points at is still in the
                // file for anybody reading it, which is exactly what somebody
                // asking for their history to be gone is asking about. It is
                // skipped where nothing went, because rewriting the whole file
                // to reclaim no pages is that cost for no reason.
                store.ReclaimFreedSpace();
            }

            return gone;
        });

        _heldYears?.Forget(userId);

        return deleted;
    }
}
