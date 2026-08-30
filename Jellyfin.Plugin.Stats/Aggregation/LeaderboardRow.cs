using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One line of a leaderboard: an account that agreed to be named, or the group
/// everybody who did not was folded into.
/// </summary>
/// <remarks>
/// The account is on the row and the name is not. A name is a fact about the
/// server now and a folded year is kept between requests, so a name written into
/// the fold would go on being handed out after the account was renamed and after
/// it was deleted, with nothing to make it let go. Whoever draws the row asks the
/// server for the name at the moment it is drawn, the same way the top lists ask
/// about access.
/// <para>
/// An account identifier is itself the personal detail on this row, which
/// <c>docs/what-is-stored.md</c> already says, so this shape is not a way round
/// issue #41's rule and does not claim to be. What permits the row is the
/// account's own recorded consent and nothing else, and the row for everybody
/// who did not consent carries no identifier at all.
/// </para>
/// </remarks>
/// <param name="UserId">Whose row it is, or null for the group everybody who has not agreed was folded into.</param>
/// <param name="Plays">How many plays fell to this row in the year.</param>
/// <param name="Watched">How long was watched across them.</param>
public sealed record LeaderboardRow(Guid? UserId, long Plays, TimeSpan Watched);
