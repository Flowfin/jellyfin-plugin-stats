using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Closes the plays a previous process left running, so each of them becomes
/// one finished row instead of staying open forever.
/// </summary>
/// <remarks>
/// A play is written to the file while it is running and replaced on every
/// check-in, so a server that stops in the middle of one leaves that row behind.
/// Nothing in the new process is tracking it: the play's events are joined on a
/// key held in memory, and that memory went with the process. Without this the
/// row sits open for as long as the file exists and the play is in no report.
/// <para>
/// The row is written exactly as the previous process last left it, which is
/// this issue's own wording for what a closed play is worth: the end is the last
/// moment the server heard from the session and the watched duration is what had
/// been accumulated by then. Nothing here invents a moment, and nothing here
/// claims the item was played through, because nothing ever said so.
/// </para>
/// <para>
/// IT RUNS BEFORE ANYTHING SUBSCRIBES TO THE SERVER'S EVENTS, and that ordering
/// is the whole of what makes it safe. Every open row on the file at that
/// instant belongs to a process that is gone, because this one has not written
/// any yet. A pass that ran later would meet rows belonging to plays that are
/// being watched right now and close them, and the stop that arrived afterwards
/// would write the play a second time.
/// </para>
/// <para>
/// The rows are read out before the first one is written. The read walks the
/// same table the write takes rows out of, a row at a time, and a walk that is
/// still open while its own table is being written to is the shape that produces
/// a partial pass. The set is bounded by how many sessions were playing when the
/// server stopped, which is a handful on a household server, so holding it is a
/// list of that size rather than a read over the history.
/// </para>
/// <para>
/// This class names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name. What that rule protects is
/// the capture switch and the per-user exclusion, which sit immediately before a
/// row is written for the first time. Nothing here is a first write: every row
/// it finishes was already judged by the gate when the play started, and this
/// only moves it from one table to the other.
/// </para>
/// <para>
/// Issue #221.
/// </para>
/// </remarks>
public sealed class FinishWhatARestartLeftOpen
{
    private readonly Func<IPlayStore> _openStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="FinishWhatARestartLeftOpen"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per pass, and what it returns is disposed of before the pass returns.</param>
    public FinishWhatARestartLeftOpen(Func<IPlayStore> openStore)
    {
        ArgumentNullException.ThrowIfNull(openStore);

        _openStore = openStore;
    }

    /// <summary>
    /// Finishes every play the file still holds as running.
    /// </summary>
    /// <remarks>
    /// A file holding none is the ordinary case and costs one statement. The
    /// store's own write takes the finished row and the open row in one
    /// transaction, so a process that stopped in the middle of this pass leaves
    /// each play either finished or still open and never both.
    /// </remarks>
    /// <returns>How many plays were finished.</returns>
    public int Run()
    {
        using var store = _openStore();

        var left = new List<OpenPlay>();
        foreach (var play in store.OpenPlays())
        {
            left.Add(play);
        }

        foreach (var play in left)
        {
            // The row is what the previous process last wrote, with one thing
            // added that it could not have known: that this is what ended it.
            // The row said nothing about how it was closed while it was open,
            // and a row finished here that went on saying nothing would be
            // counted by a report as a play whose route was never recorded,
            // which is the answer for a row from an older build rather than for
            // this one. Issue #222.
            store.AddAndForgetOpenPlay(
                play.SoFar with { ClosedBy = PlayClosedBy.ARestart },
                play.PlayKey);
        }

        return left.Count;
    }
}
