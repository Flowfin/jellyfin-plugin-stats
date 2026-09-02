namespace Jellyfin.Plugin.Stats.ScheduledTasks;

/// <summary>
/// What one retention sweep removed, counted per table.
/// </summary>
/// <remarks>
/// Two numbers rather than one, because the sweep answers two windows and the
/// rows they remove are not the same kind of thing. A play row is a record of
/// one session; a rollup is a figure standing over a day. A caller handed their
/// sum could not tell a server that deleted a year of sessions from one that
/// deleted a year of summaries, and those are different events on a server.
/// <para>
/// Issue #315.
/// </para>
/// </remarks>
public sealed record SweptRows
{
    /// <summary>
    /// Gets how many play rows the sweep deleted.
    /// </summary>
    public required long Plays { get; init; }

    /// <summary>
    /// Gets how many daily aggregates the sweep deleted.
    /// </summary>
    public required long Rollups { get; init; }
}
