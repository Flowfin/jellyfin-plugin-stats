using System;
using System.Threading;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// Deletes the play rows and the daily aggregates that are past their retention
/// windows, and gives the space they were using back to the file system.
/// </summary>
/// <remarks>
/// Two windows and one sweep. The raw rows and the figures folded from them are
/// kept for different lengths of time, so this takes two cutoffs; a second
/// scheduled task also called retention, over the same schedule and the same
/// store, is how two windows drift apart until one is quietly not running.
/// Issue #315.
/// <para>
/// The moments the windows are measured from arrive as arguments rather than
/// being read off a clock here. That is what makes each boundary a value a test
/// can choose: a sweep that read the machine clock could not be tested for the
/// day it deletes without waiting ninety days for one, and it would answer
/// differently on a runner in another zone.
/// </para>
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
    /// Deletes every daily aggregate keyed before one moment's day and every
    /// row that started before another, then reclaims the space.
    /// </summary>
    /// <remarks>
    /// A cancelled sweep leaves the rows it has already deleted deleted. There
    /// is nothing to roll back to: each bite is its own statement, which is
    /// what makes the sweep interruptible in the first place, and a row past
    /// its retention window was going to go on the next run anyway. What a
    /// cancellation does cost is the reclaim, so a sweep stopped halfway
    /// leaves a file that is no smaller than it was.
    /// <para>
    /// The aggregates go first, and that order is the property this run has to
    /// hold rather than an arrangement of two loops. Where the aggregate window
    /// is the shorter of the two, every rollup this deletes can still be folded
    /// again from rows that are in the file, which is what makes that
    /// configuration a recoverable one. Deleting the play rows first would take
    /// the source rows of the oldest of those days away in the same pass and
    /// make the deletion terminal, on a server whose settings say it is not.
    /// </para>
    /// </remarks>
    /// <param name="cutoffUtc">The moment, in UTC. A row that started before it is deleted.</param>
    /// <param name="aggregateCutoffUtc">
    /// The moment, in UTC, whose day in the store's rollup zone is the first day
    /// of aggregates that are kept. An aggregate keyed before that day is
    /// deleted. A store that has keyed no aggregate states no zone, and none is
    /// deleted there.
    /// </param>
    /// <param name="progress">Told how far through the sweep is, from nothing to a hundred.</param>
    /// <param name="cancellationToken">Checked between bites.</param>
    /// <returns>How many of each were deleted.</returns>
    public SweptRows Run(
        DateTime cutoffUtc,
        DateTime aggregateCutoffUtc,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        using var store = _openStore();

        // The day the aggregate window ends on is a day in the zone the store
        // states its rollups were counted in, and not one in the machine's. A
        // cutoff converted through a runner's zone is a boundary that moves by
        // one day depending on where the server is, on a deletion.
        var zone = store.RollupZone;
        var oldestDayKept = zone is null
            ? (DateOnly?)null
            : LocalDay.Of(new DateTimeOffset(aggregateCutoffUtc.ToUniversalTime(), TimeSpan.Zero), zone);

        // Both counts before either loop, so the fraction below is over the
        // whole sweep rather than restarting at nought half way through it.
        var doomed = store.CountPlaysStartedBefore(cutoffUtc)
            + (oldestDayKept is null ? 0 : store.CountRollupsBefore(oldestDayKept.Value));

        progress.Report(0);

        long deleted = 0;
        long rollups = 0;
        while (oldestDayKept is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Retention, and the same retention the play rows go under. What
            // this deletion says is that the aggregate is past the window
            // configured for it, which is the reason a play row goes too, and
            // recording it as corrective would give one reason two names
            // depending on which table it happened in. Corrective belongs to the
            // deletion that is already there for it, the one that drops a day a
            // corrective deletion emptied.
            var bitten = store.DeleteRollupsBefore(oldestDayKept.Value, DeletionClass.Retention, _bite);
            if (bitten == 0)
            {
                break;
            }

            rollups += bitten;
            deleted += bitten;

            progress.Report(Math.Min(99d, deleted * 100d / doomed));
        }

        long plays = 0;
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

            plays += bitten;
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

        return new SweptRows { Plays = plays, Rollups = rollups };
    }
}
