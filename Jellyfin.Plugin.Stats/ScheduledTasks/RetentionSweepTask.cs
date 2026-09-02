using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Configuration;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// The retention sweep as the server sees it: an entry in the task list, on a
/// daily trigger, that an administrator can also run by hand.
/// </summary>
/// <remarks>
/// Everything this needs arrives through the constructor, and every one of
/// those is registered by <see cref="PluginServiceRegistrator"/>. That is not a
/// style preference. The server builds every scheduled task it finds in a
/// plugin assembly out of its own container, and an argument it cannot resolve
/// does not merely lose the task, it fails the plugin and takes the settings
/// page with it:
/// <para>
/// <c>ApplicationHost.cs</c> at <c>v10.11.11</c> catches the failure to create
/// an export and calls <c>_pluginManager.FailPlugin(type.Assembly)</c>. So a
/// registration removed from the registrator is not a task that quietly stops
/// running; it is a plugin that stops loading, and
/// <c>TheTaskIsBuiltTheWayTheServerBuildsIt</c> in the suite is what catches
/// that here instead of there.
/// </para>
/// <para>
/// Both windows are read at the run rather than held from start-up, so a
/// retention changed on the settings page decides the next sweep and not the
/// one after a restart.
/// </para>
/// </remarks>
public sealed class RetentionSweepTask : IScheduledTask
{
    /// <summary>
    /// When the daily trigger fires, as a time of day on the server.
    /// </summary>
    /// <remarks>
    /// Small hours, because a sweep rewrites the store file at the end and that
    /// is the one moment in its run when nothing else can write a row.
    /// </remarks>
    private static readonly TimeSpan WhenItRuns = TimeSpan.FromHours(3);

    private readonly RetentionSweep _sweep;
    private readonly TimeProvider _clock;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetentionSweepTask"/> class.
    /// </summary>
    /// <param name="sweep">The sweep itself.</param>
    /// <param name="clock">Where the moment the window is measured back from comes from.</param>
    /// <param name="configuration">Reads the current configuration, at every run rather than once.</param>
    public RetentionSweepTask(
        RetentionSweep sweep,
        TimeProvider clock,
        Func<PluginConfiguration> configuration)
    {
        _sweep = sweep;
        _clock = clock;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public string Name => "Delete playback statistics past their retention windows";

    /// <inheritdoc />
    /// <remarks>
    /// The server keeps a task's triggers against this key, so changing it
    /// gives every existing installation a task with no history and leaves the
    /// old triggers pointing at nothing.
    /// </remarks>
    public string Key => "PlaybackStatisticsRetentionSweep";

    /// <inheritdoc />
    public string Description =>
        "Deletes play rows and daily aggregates older than the two retention windows on the plugin's settings page, then gives the space they were using back to the disk. The deletion is permanent.";

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
    /// task manager called this from. Opening a file, taking a write lock and
    /// rewriting a database are all file work, and none of it belongs on a
    /// caller's thread whatever that caller turns out to be.
    /// <para>
    /// Nothing is written to the log, on any path, and the count of rows
    /// deleted is returned to the sweep's caller rather than reported there. A
    /// daily line saying how many plays a server produced is a record of how
    /// much a household watched, kept in a file none of this plugin's retention
    /// settings reach, and <c>docs/what-the-log-contains.md</c> is where that
    /// argument is made for every other path.
    /// </para>
    /// </remarks>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // One reading of the clock for both windows. Two calls could land on
        // either side of a midnight, which would measure the two boundaries
        // from different days on one run of one task.
        var now = _clock.GetUtcNow().UtcDateTime;

        var configuration = _configuration();
        var cutoff = now.AddDays(-configuration.PlayRowRetentionDays);
        var aggregateCutoff = now.AddDays(-configuration.DailyAggregateRetentionDays);

        await Task
            .Run(() => _sweep.Run(cutoff, aggregateCutoff, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }
}
