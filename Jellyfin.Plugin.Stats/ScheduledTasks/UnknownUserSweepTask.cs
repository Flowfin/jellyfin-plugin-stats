using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// The sweep for accounts the server no longer has, as the server sees it: an
/// entry in the task list, on a daily trigger, that an administrator can also
/// run by hand.
/// </summary>
/// <remarks>
/// Everything this needs arrives through the constructor, and it is registered
/// by <see cref="PluginServiceRegistrator"/>. The server builds every scheduled
/// task it finds in a plugin assembly out of its own container and fails the
/// whole plugin over an argument it cannot resolve, taking the settings page
/// with it, so a registration missing here is not a task that quietly stops
/// running. <c>TheTaskIsBuiltTheWayTheServerBuildsIt</c> in the suite is what
/// catches that here instead of there.
/// </remarks>
public sealed class UnknownUserSweepTask : IScheduledTask
{
    /// <summary>
    /// When the daily trigger fires, as a time of day on the server.
    /// </summary>
    /// <remarks>
    /// An hour after the retention sweep rather than beside it. Both end in a
    /// rewrite of the same file, which is the one moment in either run when
    /// nothing else can write a row, and two of them overlapping would have one
    /// waiting on the other's write lock for as long as the rewrite takes.
    /// </remarks>
    private static readonly TimeSpan WhenItRuns = TimeSpan.FromHours(4);

    private readonly UnknownUserSweep _sweep;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnknownUserSweepTask"/>
    /// class.
    /// </summary>
    /// <param name="sweep">The sweep itself.</param>
    public UnknownUserSweepTask(UnknownUserSweep sweep)
    {
        _sweep = sweep;
    }

    /// <inheritdoc />
    public string Name => "Delete playback statistics belonging to accounts the server no longer has";

    /// <inheritdoc />
    /// <remarks>
    /// The server keeps a task's triggers against this key, so changing it
    /// gives every existing installation a task with no history and leaves the
    /// old triggers pointing at nothing. It is also a name in a namespace this
    /// plugin shares with every other plugin installed beside it, which is what
    /// issue #83 compares across a set.
    /// </remarks>
    public string Key => "PlaybackStatisticsUnknownUserSweep";

    /// <inheritdoc />
    public string Description =>
        "Finds play rows belonging to accounts this server does not have any more, deletes them, and gives the space they were using back to the disk. It is the sweep that catches a user deleted while this plugin was not running. The deletion is permanent.";

    /// <inheritdoc />
    public string Category => "Playback statistics";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = WhenItRuns.Ticks
        }
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The sweep runs on a thread of its own rather than on whichever one the
    /// task manager called this from. Opening a file, asking the server about
    /// each account, taking a write lock and rewriting a database are all work
    /// that does not belong on a caller's thread whatever that caller turns out
    /// to be.
    /// <para>
    /// Nothing is written to the log, on any path. How many rows a server was
    /// holding for accounts it no longer has is a statement about who used to
    /// watch what, kept in a file none of this plugin's retention settings
    /// reach, and <c>docs/what-the-log-contains.md</c> is where that argument is
    /// made for every other path. The count goes to the sweep's caller.
    /// </para>
    /// </remarks>
    /// <param name="progress">Told how far through the sweep is.</param>
    /// <param name="cancellationToken">Passed on, and checked between lookups and between bites.</param>
    /// <returns>A task that completes when the sweep has.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await Task
            .Run(() => _sweep.Run(progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }
}
