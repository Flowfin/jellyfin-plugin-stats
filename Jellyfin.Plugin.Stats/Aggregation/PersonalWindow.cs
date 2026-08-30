namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Which stretch of time one person's own figures are read over.
/// </summary>
/// <remarks>
/// Three members and not a range a caller sends. A range on the request would be
/// the reader deciding how much of the store one request reads, which is what
/// issue #55 refuses across this plugin; three named windows are three costs
/// this plugin already knows how to pay.
/// <para>
/// Each member also fixes what the window is grouped by, because the grouping is
/// a fact about the window rather than a second choice: thirty days grouped by
/// month is one bar, and a year grouped by day is three hundred and sixty-six of
/// them. Issue #274.
/// </para>
/// </remarks>
public enum PersonalWindow
{
    /// <summary>
    /// The last thirty days ending today, grouped by day.
    /// </summary>
    Last30Days,

    /// <summary>
    /// The last twelve months ending with the current one, grouped by month.
    /// </summary>
    Last12Months,

    /// <summary>
    /// Everything the store still holds for this account, grouped by nothing.
    /// </summary>
    /// <remarks>
    /// What "everything" reaches is what the store has not swept, and that is
    /// narrower than what the account ever watched. The answer says which window
    /// it covers rather than claiming to be a life.
    /// </remarks>
    AllTime,
}
