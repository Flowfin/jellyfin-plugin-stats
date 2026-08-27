using System;
using System.Threading;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// Deletes the play rows that are past their retention window, and gives the
/// space they were using back to the file system.
/// </summary>
/// <remarks>
/// The moment the window is measured from arrives as an argument rather than
/// being read off a clock here. That is what makes the boundary a value a test
/// can choose: a sweep that read the machine clock could not be tested for the
/// day it deletes without waiting ninety days for one, and it would answer
/// differently on a runner in another zone.
/// <para>
/// The rows go a bite at a time. One statement deleting a decade of history
/// holds the store's write lock for its whole duration and answers no
/// cancellation while it runs, which is exactly the first sweep on a server
/// that has been recording for years: the one an administrator is most likely
/// to want to stop.
/// </para>
/// <para>
/// This class names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name. What that rule protects
/// is the capture switch and the per-user exclusion, which sit immediately
/// before a write; nothing here writes a row, and a rule that let this file
/// through by narrowing its pattern would have stopped protecting anything.
/// </para>
/// </remarks>
public sealed class RetentionSweep
{
    /// <summary>
    /// How many rows one bite takes.
    /// </summary>
    /// <remarks>
    /// Small enough that a cancellation is answered promptly and large enough
    /// that a year of rows is not ten thousand statements. It is named here
    /// rather than left to a caller because a size chosen per call site is a
    /// size nobody can state.
    /// </remarks>
    public const int DefaultBite = 500;

    private readonly Func<IPlayStore> _openStore;
    private readonly int _bite;
    private readonly HeldYears? _heldYears;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionSweep"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per sweep, and what it returns is disposed of before the sweep returns.</param>
    /// <param name="bite">How many rows one statement deletes.</param>
    /// <param name="heldYears">
    /// What is keeping folded years, told to let all of them go where this
    /// sweep deleted anything. Null where nothing is keeping any.
    /// </param>
    public RetentionSweep(Func<IPlayStore> openStore, int bite, HeldYears? heldYears = null)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bite);

        _openStore = openStore;
        _bite = bite;
        _heldYears = heldYears;
    }

    /// <summary>
    /// Deletes every row that started before a moment, then reclaims the space.
    /// </summary>
    /// <remarks>
    /// A cancelled sweep leaves the rows it has already deleted deleted. There
    /// is nothing to roll back to: each bite is its own statement, which is
    /// what makes the sweep interruptible in the first place, and a row past
    /// its retention window was going to go on the next run anyway. What a
    /// cancellation does cost is the reclaim, so a sweep stopped halfway
    /// leaves a file that is no smaller than it was.
    /// </remarks>
    /// <param name="cutoffUtc">The moment, in UTC. A row that started before it is deleted.</param>
    /// <param name="progress">Told how far through the sweep is, from nothing to a hundred.</param>
    /// <param name="cancellationToken">Checked between bites.</param>
    /// <returns>How many rows were deleted.</returns>
    public long Run(DateTime cutoffUtc, IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        using var store = _openStore();

        var doomed = store.CountPlaysStartedBefore(cutoffUtc);
        progress.Report(0);

        long deleted = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Retention, because what this sweep says is that the raw rows
            // have aged out and not that the plays stop being counted. A figure
            // computed while they were there stands, which is what the longer
            // aggregate window exists for: the daily sweep at the default
            // ninety days would otherwise invalidate aggregates about three
            // hundred days before their own expiry, on every installation
            // running defaults, and take that setting out of service without
            // anybody deciding to remove it.
            //
            // It stays retention whatever the sweep happens to remove. A cutoff
            // that takes the last rows of an account somebody deleted this
            // morning is still the window doing its work, and reading the class
            // off which rows went is what issue #251 exists to refuse.
            var bitten = store.DeletePlaysStartedBefore(cutoffUtc, DeletionClass.Retention, _bite);
            if (bitten == 0)
            {
                break;
            }

            deleted += bitten;

            // Ninety-nine at most until the reclaim has run, because the
            // reclaim is the step this sweep is judged on and a hundred before
            // it is a sweep reporting itself finished with its last act still
            // to come. The count was taken before the first bite, so a row
            // written into the window while the sweep runs can carry the
            // fraction past one; the ceiling is what keeps that off the page.
            progress.Report(Math.Min(99d, deleted * 100d / doomed));
        }

        store.ReclaimFreedSpace();

        // Every folded year rather than any narrower set, and only where rows
        // actually went. The cutoff names a moment and not an account, so every
        // account is a candidate and there is nothing here that says which of
        // them lost a row. A sweep that deleted nothing is the ordinary run on
        // a server inside its window, and throwing away every held answer on it
        // would be a daily cost for no change.
        if (deleted > 0)
        {
            _heldYears?.ForgetEverything();
        }

        progress.Report(100);

        return deleted;
    }
}
