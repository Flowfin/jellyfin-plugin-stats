using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// The sweep for plays whose sessions have gone quiet, as the server sees it: an
/// entry in the task list, on an interval, that an administrator can also run by
/// hand.
/// </summary>
/// <remarks>
/// Everything this needs arrives through the constructor, and it is registered
/// by <see cref="PluginServiceRegistrator"/>. The server builds every scheduled
/// task it finds in a plugin assembly out of its own container and fails the
/// whole plugin over an argument it cannot resolve, taking the settings page
/// with it, so a registration missing here is not a task that quietly stops
/// running.
/// <para>
/// It is on an interval and not on a daily trigger, which is the difference
/// between this task and the two beside it. Those two rewrite a file and are
/// worth doing once a night; this one decides how long a play that nobody will
/// ever stop waits to appear in a report, so a daily trigger would make the
/// answer to whether an interrupted play is counted depend on what time of day
/// the client died.
/// </para>
/// <para>
/// Issue #221.
/// </para>
/// </remarks>
public sealed class QuietPlaySweepTask : IScheduledTask
{
    /// <summary>
    /// How often the sweep runs.
    /// </summary>
    /// <remarks>
    /// Shorter than the bound it applies, so a play that has gone quiet waits
    /// the bound and a little rather than the bound and a whole interval.
    /// Nothing here opens a file or reads a row: a run over a server where every
    /// session is alive walks the plays in memory and writes nothing.
    /// </remarks>
    private static readonly TimeSpan HowOftenItRuns = TimeSpan.FromMinutes(10);

    private readonly QuietPlaySweep _sweep;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuietPlaySweepTask"/> class.
    /// </summary>
    /// <param name="sweep">The sweep itself.</param>
    public QuietPlaySweepTask(QuietPlaySweep sweep)
    {
        _sweep = sweep;
    }

    /// <inheritdoc />
    public string Name => "Close playback statistics for sessions that stopped reporting";

    /// <inheritdoc />
    /// <remarks>
    /// The server keeps a task's triggers against this key, so changing it
    /// gives every existing installation a task with no history and leaves the
    /// old triggers pointing at nothing. It is also a name in a namespace this
    /// plugin shares with every other plugin installed beside it, which is what
    /// issue #83 compares across a set.
    /// </remarks>
    public string Key => "PlaybackStatisticsQuietPlaySweep";

    /// <inheritdoc />
    public string Description =>
        "Finds plays that started and never stopped because the client went away without telling the server, and records each one as ending at the last moment the server heard from it. Without it those plays would never appear in a report.";

    /// <inheritdoc />
    public string Category => "Playback statistics";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = HowOftenItRuns.Ticks
        }
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The sweep runs on the thread the task manager called this from, unlike
    /// the two sweeps beside it. What it does is walk a dictionary under a lock
    /// and hand what it finds to the write path, which is what every playback
    /// event on this server already does on the server's own event thread, and
    /// the write itself happens on the writer's queue rather than here.
    /// <para>
    /// Nothing is written to the log, on any path. How many plays a server was
    /// holding open is a statement about who was watching what,
    /// <c>docs/what-the-log-contains.md</c> is where that argument is made for
    /// every other path, and the count goes to the sweep's caller.
    /// </para>
    /// </remarks>
    /// <param name="progress">Told the sweep is finished.</param>
    /// <param name="cancellationToken">Not read: the sweep is one pass over what is in memory and has nothing to stop between.</param>
    /// <returns>A task that completes when the sweep has.</returns>
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        _sweep.Run();
        progress.Report(100);

        return Task.CompletedTask;
    }
}
