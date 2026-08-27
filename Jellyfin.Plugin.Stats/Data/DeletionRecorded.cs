namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// One deletion the store performed, as a later reader meets it.
/// </summary>
/// <remarks>
/// What this exists for is the retrofit that cannot be done. A figure computed
/// from play rows outlives the rows, so a reader asking later whether a gap in
/// the rows should have moved a figure has only the rows to look at, and the
/// rows are gone. Nothing in a store that recorded no class can answer it, and
/// no amount of reading the file afterwards recovers what was never written.
/// <para>
/// Issue #251.
/// </para>
/// </remarks>
public sealed record DeletionRecorded
{
    /// <summary>
    /// Gets what the deletion said about the rows it removed.
    /// </summary>
    public required DeletionClass Class { get; init; }

    /// <summary>
    /// Gets how many rows that call removed.
    /// </summary>
    /// <remarks>
    /// Always more than nought. A call that removed nothing is a caller
    /// discovering it has finished biting, and recording one row per such call
    /// would fill this table with entries saying that nothing happened.
    /// </remarks>
    public required int Rows { get; init; }
}
