namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// Which of a top list's two figures decides the order, and therefore which
/// rows survive the cut.
/// </summary>
/// <remarks>
/// Both are offered because they disagree, and the disagreement is the
/// interesting part: a series somebody left running all evening is many plays
/// and little watching, and a long film watched once is the other way round.
/// Every row carries both figures whichever is chosen, so a reader who wants
/// the other reading of the rows it was handed has it; what the order changes
/// is which rows are in the answer at all once it has been cut. Issue #52.
/// </remarks>
public enum TopListOrder
{
    /// <summary>
    /// Most watched time first.
    /// </summary>
    WatchedTime,

    /// <summary>
    /// Most plays first.
    /// </summary>
    Plays
}
