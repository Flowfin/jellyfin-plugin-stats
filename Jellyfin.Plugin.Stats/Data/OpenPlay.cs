namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// One play that has started and has not stopped, as the file holds it.
/// </summary>
/// <remarks>
/// An open play is the same shape as a finished one plus the key its events are
/// joined on. It is not a second row shape to keep in step: a play that is still
/// running has every field a finished play has, and the two that cannot be known
/// yet are the ones the reader is told to read differently rather than the ones
/// left out. Issue #220.
/// <para>
/// <see cref="PlayRecord.EndedUtc"/> on an open play is the last moment the
/// server heard from the session, and it moves forward with every progress
/// report. <see cref="PlayRecord.ReachedTheEnd"/> is false, because nothing has
/// said the item was played through and the server only says so on the stop.
/// Neither is a claim that the play ended; what says a play ended is the row
/// being in the finished table instead of this one.
/// </para>
/// <para>
/// It lives in a table of its own rather than beside the finished rows. Every
/// read in this plugin answers a question about plays that happened, and a
/// not-yet-play in that table makes every one of those reads wrong unless it
/// remembers a condition. The set of reads grows with every report this plan
/// adds and the set of removals does not, so the omission that costs least is
/// the one on the removal side, and the store covers that itself rather than
/// asking each caller to.
/// </para>
/// </remarks>
public sealed record OpenPlay
{
    /// <summary>
    /// Gets the key this play's events are joined on, which is the server's own
    /// play session identifier where there is one and the device and item
    /// together where there is not.
    /// </summary>
    /// <remarks>
    /// It is the identity of the row rather than a field on the play. Writing
    /// the same key again replaces what was there, which is what keeps one
    /// running play to one row however often its session reports.
    /// </remarks>
    public required string PlayKey { get; init; }

    /// <summary>
    /// Gets the play as it stands, with everything the server has said about it
    /// so far.
    /// </summary>
    public required PlayRecord SoFar { get; init; }
}
