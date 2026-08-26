using System;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// What one day held for one account, one kind of item and one client.
/// </summary>
/// <remarks>
/// A rollup is never the only copy of anything. Every figure here is one a play
/// row carries or one that follows from the play rows alone, so a rebuild can
/// produce this row again from the rows it stands over and a reader can compare
/// the two. A figure that could not be produced that way does not belong here,
/// whatever it would be worth: it would make the rollup the only record of
/// something, and a table that is the only record of something cannot be
/// rebuilt after a deletion touches it.
/// <para>
/// The day is a local day and not a fact about the play. Which day a play falls
/// on depends on whose midnight is meant, so the zone every day here was counted
/// in is stated once for the whole table rather than assumed by each reader, and
/// the store is where a reader asks for it. It is deliberately not a field here:
/// a zone on each row would let one file hold two answers for one day.
/// </para>
/// <para>
/// The four delivery counts are the four the delivery fold already distinguishes
/// rather than the two a reader usually wants. Transcoded is one of them and
/// direct is the sum of two, so both are available by addition, and an account
/// of a day that folded them here would have folded away the difference between
/// a play the server repackaged and one it re-encoded, and would have had
/// nowhere to put a play whose method was never reported.
/// </para>
/// </remarks>
public sealed record DailyRollup
{
    /// <summary>
    /// Gets the day, in the zone the store states its rollups are counted in.
    /// </summary>
    public required DateOnly Day { get; init; }

    /// <summary>
    /// Gets the account the plays behind this row belong to.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the kind of item those plays were of.
    /// </summary>
    public required string ItemType { get; init; }

    /// <summary>
    /// Gets the client they were played on.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// Gets how many plays this row stands over.
    /// </summary>
    public required long Plays { get; init; }

    /// <summary>
    /// Gets how long was watched across them.
    /// </summary>
    /// <remarks>
    /// A duration and not a count of seconds, because every other duration this
    /// store holds is one and a second unit in one table is a rounding somebody
    /// eventually compares against the rows. The rebuild that has to reproduce
    /// this figure adds the same durations, so the two agree exactly rather than
    /// to the nearest second.
    /// </remarks>
    public required TimeSpan Watched { get; init; }

    /// <summary>
    /// Gets how many of those plays reached the end of the item.
    /// </summary>
    public required long Completed { get; init; }

    /// <summary>
    /// Gets how many of them started with no delivery method reported.
    /// </summary>
    public required long UnknownMethod { get; init; }

    /// <summary>
    /// Gets how many of them started as a direct play.
    /// </summary>
    public required long DirectPlay { get; init; }

    /// <summary>
    /// Gets how many of them started as a direct stream.
    /// </summary>
    public required long DirectStream { get; init; }

    /// <summary>
    /// Gets how many of them started as a transcode.
    /// </summary>
    public required long Transcode { get; init; }
}
