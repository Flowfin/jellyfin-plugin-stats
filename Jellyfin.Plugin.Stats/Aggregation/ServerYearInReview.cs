namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// The wrap-up for the whole server: the same figures a person's year answers
/// with, folded over every play, beside the two breakdowns a person's year has
/// no use for and the one shape that may name anybody.
/// </summary>
/// <remarks>
/// The figures name items, series and days and never an account, because the
/// fold that produced them keeps no key for who watched. That is what lets this
/// answer stand on a server where nobody has agreed to be named: none of the
/// item, series, client or reason figures is per account, so a server with no
/// consent recorded anywhere loses the leaderboard and nothing else. Issue #68's
/// second condition is that sentence.
/// <para>
/// The figures are NOT the sum over the per-account wrap-ups, and a reader who
/// takes them for it has the proof backwards. They are one fold over one read of
/// the rows; that they agree with the sum over the accounts is the property
/// issue #68's third condition asserts, and a figure defined as the sum could
/// not be asserted against it.
/// </para>
/// <para>
/// THREE OF THE FOUR ARE ABSENT RATHER THAN EMPTY WHERE THE YEAR COULD NOT BE
/// READ. A breakdown folded over the rows a refused read did not return would
/// answer nought plays, and a nought and an unknown are different statements a
/// reader cannot tell apart once one has been written as the other. Only
/// <see cref="Figures"/> is always present, because it is the one that carries
/// the reason.
/// </para>
/// </remarks>
/// <param name="Figures">The year over every play, naming no account.</param>
/// <param name="Clients">How the year divided between the clients it was watched on, or null where answering it would name somebody.</param>
/// <param name="Reasons">Why the server transcoded what it transcoded, with how many plays recorded any reason at all, or null where the year could not be read.</param>
/// <param name="Leaderboard">Who watched most among the accounts that agreed to be named, or null where answering it would name whoever did not, or where the year could not be read.</param>
public sealed record ServerYearInReview(
    YearInReview Figures,
    DimensionBreakdown? Clients,
    TranscodeReasonBreakdown? Reasons,
    ConsentedLeaderboard? Leaderboard);
