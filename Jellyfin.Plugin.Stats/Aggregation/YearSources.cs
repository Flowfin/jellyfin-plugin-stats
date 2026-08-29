namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Where a wrap-up's figures came from, said on the answer rather than known
/// by whoever wrote the fold.
/// </summary>
/// <remarks>
/// A year is folded from two places and it cannot be folded from one. The
/// day-by-day rollups carry the plays, the watched time, the completions and
/// the delivery counts, and they carry nothing that names an item, because a
/// rollup holds only what a rebuild can produce again from the rows. So the
/// distinct items, the longest single play and the two top lists are read from
/// the play rows and the rest is read from the aggregates.
/// <para>
/// The two have different reach and that is why this is on the answer.
/// <see cref="YearCoverage"/> states what the store lost, and it is one window;
/// a figure read out of an aggregate that outlived its rows is covered further
/// back than that window says. A reader given one set of figures and one window
/// cannot tell which of them the raw rows still support, so a wrap-up drawing
/// from both owes this second statement. Issue #254.
/// </para>
/// <para>
/// It is also where a figure that was not computed says so. A year is a range
/// the caller cannot shorten, so refusing the whole wrap-up because one month
/// holds more plays than a read may hold would be a permanent refusal of
/// somebody's own history. The wrap-up walks the year in fixed windows instead,
/// each read under the existing bound, and a window over that bound degrades
/// exactly the figures it would have fed, with the reason beside them. A year
/// with one honest gap beats a year refused. Issue #66.
/// </para>
/// </remarks>
public sealed record YearSources
{
    /// <summary>
    /// What a figure read out of the day-by-day rollups says here.
    /// </summary>
    public const string Aggregates = "aggregates";

    /// <summary>
    /// What a figure read out of the play rows says here.
    /// </summary>
    public const string Plays = "plays";

    /// <summary>
    /// What a figure nothing could be read for says here. The reason is beside
    /// it, because a figure that is absent and one that could not be taken are
    /// different statements and a reader cannot tell them apart from a null.
    /// </summary>
    public const string NotComputed = "not computed";

    /// <summary>
    /// Gets where the plays, the watched time, the completions, the busiest day,
    /// the busiest month and the delivery split came from.
    /// </summary>
    /// <remarks>
    /// <see cref="Aggregates"/> on a store whose rollups are keyed in the zone
    /// the year is being read in, <see cref="Plays"/> where they are not, and
    /// <see cref="NotComputed"/> where the rows they would have been read from
    /// could not be read under the bound.
    /// </remarks>
    public required string Totals { get; init; }

    /// <summary>
    /// Gets where the distinct items, the longest single play and the two top
    /// lists came from.
    /// </summary>
    /// <remarks>
    /// Never <see cref="Aggregates"/>: a rollup carries no column any of these
    /// four could be read out of, and one that did would make the table the only
    /// record of something it could not be rebuilt from.
    /// </remarks>
    public required string Detail { get; init; }

    /// <summary>
    /// Gets why a group of figures was not computed, where one was not.
    /// </summary>
    public string? NotComputedBecause { get; init; }
}
