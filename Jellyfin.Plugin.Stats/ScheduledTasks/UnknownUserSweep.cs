using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// Deletes the play rows belonging to accounts the server does not have any
/// more, and gives the space they were using back to the file system.
/// </summary>
/// <remarks>
/// The deletion that runs when an account is removed only fires while this
/// plugin is loaded. A user deleted while the plugin was disabled, between an
/// uninstall and a reinstall, or before it was ever installed, leaves rows
/// nothing heard about and nothing counts. This is the sweep that finds them,
/// and it is the reason the promise about deletion is one an operator can keep
/// rather than one that holds while the plugin happened to be running.
/// <para>
/// The store is asked which identifiers it holds and the server is asked about
/// each of them, which is the only direction that works: the other one, taking
/// the accounts the server has and deleting everything else, is a set
/// difference computed from a list that is authoritative only while it is
/// complete.
/// </para>
/// <para>
/// EVERY LOOKUP IS MADE BEFORE THE FIRST DELETION. An identifier the server
/// cannot answer for because the lookup failed is not an identifier whose
/// account is gone, and a sweep that deleted as it walked would have removed
/// live rows before the failure reached anybody. Asking first means a failure
/// half way through costs a run rather than somebody's history, and there is
/// nothing to restore it from: the rows are gone from the file and this plugin
/// keeps no second copy.
/// </para>
/// <para>
/// The rows go a bite at a time, like the retention sweep's and the account
/// deletion's. A server one person uses holds all of its rows under one
/// identifier, so the set behind a single identifier is not small by
/// construction, and one statement over years of rows holds the store's write
/// lock for its whole duration.
/// </para>
/// <para>
/// This class names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name for the same reason the
/// retention sweep and the account deletion are. What that rule protects is the
/// capture switch and the per-user exclusion, which sit immediately before a
/// write, and nothing here writes a row.
/// </para>
/// </remarks>
public sealed class UnknownUserSweep
{
    /// <summary>
    /// How many rows one bite takes.
    /// </summary>
    /// <remarks>
    /// The number the other two deletions use, and named here rather than taken
    /// from either of them, because two callers reading one constant makes a
    /// change to either one a change to both.
    /// </remarks>
    public const int DefaultBite = 500;

    private readonly Func<IPlayStore> _openStore;
    private readonly IUserManager _users;
    private readonly int _bite;
    private readonly HeldYears? _heldYears;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownUserSweep"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per sweep, and what it returns is disposed of before the sweep returns.</param>
    /// <param name="users">The accounts the server currently has, asked one identifier at a time.</param>
    /// <param name="bite">How many rows one statement deletes.</param>
    /// <param name="heldYears">
    /// What is keeping folded years, told to let go of each account this sweep
    /// finds the server no longer has. Null where nothing is keeping any.
    /// </param>
    public UnknownUserSweep(Func<IPlayStore> openStore, IUserManager users, int bite, HeldYears? heldYears = null)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bite);

        _openStore = openStore;
        _users = users;
        _bite = bite;
        _heldYears = heldYears;
    }

    /// <summary>
    /// Deletes every row whose user the server no longer has, then reclaims the
    /// space where anything went.
    /// </summary>
    /// <remarks>
    /// A server with no accounts at all is not guarded against, and that is a
    /// decision rather than an omission. Every row in the store belongs to an
    /// identifier, so a server that has no accounts has no rows that belong to
    /// anybody, and refusing to sweep there would leave exactly the history
    /// this task exists to remove. What the guard would protect against is a
    /// user manager answering emptily while the server is still assembling
    /// itself, and the shape that covers that case honestly is the one above:
    /// a lookup that fails throws rather than answering that nobody is there.
    /// </remarks>
    /// <param name="progress">Told how far through the sweep is, from nothing to a hundred.</param>
    /// <param name="cancellationToken">Checked between lookups and between bites.</param>
    /// <returns>How many rows were deleted.</returns>
    public long Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        using var store = _openStore();

        progress.Report(0);

        var gone = new List<Guid>();
        foreach (var userId in store.UserIdsWithPlays())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_users.GetUserById(userId) is null)
            {
                gone.Add(userId);
            }
        }

        long deleted = 0;
        for (var i = 0; i < gone.Count; i++)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bitten = store.DeletePlaysFor(gone[i], _bite);
                if (bitten == 0)
                {
                    break;
                }

                deleted += bitten;
            }

            // Named here rather than once at the end, because this sweep knows
            // exactly which accounts it took rows from and a held year for an
            // account the server no longer has is the same figure about a
            // departed person the deletion route lets go of.
            _heldYears?.Forget(gone[i]);

            // Ninety-nine at most until the reclaim has run, for the reason the
            // retention sweep reports the same ceiling: the reclaim is the last
            // act of a run and a hundred before it is a sweep reporting itself
            // finished with work still to do.
            progress.Report(Math.Min(99d, (i + 1) * 100d / gone.Count));
        }

        // Skipped where nothing was deleted, because a rewrite of the file to
        // reclaim no pages is that whole cost for no reason, and this task
        // finds nothing on almost every run it ever makes.
        if (deleted > 0)
        {
            store.ReclaimFreedSpace();
        }

        progress.Report(100);

        return deleted;
    }
}
