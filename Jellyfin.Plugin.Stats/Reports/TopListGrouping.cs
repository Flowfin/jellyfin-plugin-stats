namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// What a top list counts under one row.
/// </summary>
/// <remarks>
/// A closed set of two, and an account is not in it. Both members group on an
/// identifier the row already carries, so neither asks the library anything and
/// neither depends on what a library holds today. Issue #52.
/// </remarks>
public enum TopListGrouping
{
    /// <summary>
    /// One row per item, keyed on the item's identifier. An episode is itself
    /// here and never its series.
    /// </summary>
    Item,

    /// <summary>
    /// One row per series, keyed on the parent an episode names. A play that
    /// names no parent falls out of this list rather than becoming a row of its
    /// own, because a film counted as a series of one is a statement nobody
    /// asked for.
    /// </summary>
    Series
}
