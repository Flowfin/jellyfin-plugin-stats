using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Who watched most, over the accounts that agreed to be named, with everybody
/// else as one group carrying no identifier.
/// </summary>
/// <remarks>
/// This is the only shape in the plugin that puts an account on a server-wide
/// answer, and the only thing that puts one there is that account's own
/// recorded consent. An administrator cannot record it and cannot read the
/// detail of somebody who has not, which is issue #42's endpoint and its
/// authorization matrix; withdrawing it takes the account off this shape on the
/// next request, because the register is read while the answer is folded rather
/// than cached beside it.
/// <para>
/// THE COMBINED GROUP IS WHY THIS CAN BE WITHHELD ENTIRELY. Named rows beside a
/// server total are a subtraction: what is left over after the named rows is
/// whoever did not agree, and where that remainder stands on one account the
/// group is that account under a different name. Giving it no identifier does
/// not change who it is about. So where anybody was folded into the group and
/// fewer accounts stand behind it than a row needs, there is no leaderboard at
/// all - the same threshold and the same reasoning as the dimension breakdown
/// issue #41 landed, applied to the one dimension that is an account.
/// </para>
/// <para>
/// A leaderboard is withheld rather than trimmed. Trimming the named rows until
/// the remainder is thick enough would answer with a list whose absences are
/// themselves readable, and a reader could not tell a trimmed list from a
/// complete one. Issue #68.
/// </para>
/// </remarks>
public sealed record ConsentedLeaderboard
{
    private ConsentedLeaderboard(IReadOnlyList<LeaderboardRow> rows, long accountsFolded)
    {
        Rows = rows;
        AccountsFolded = accountsFolded;
    }

    /// <summary>
    /// Gets the rows, most watched first, with the group everybody who has not
    /// agreed was folded into last where there is one.
    /// </summary>
    /// <remarks>
    /// The group is last by construction rather than by where its watched time
    /// happens to put it, because a group that sorted in among the named rows
    /// would let a reader read its size off its position.
    /// </remarks>
    public IReadOnlyList<LeaderboardRow> Rows { get; }

    /// <summary>
    /// Gets how many accounts stand behind the combined group, which is nought
    /// where every account with a play in the year had agreed.
    /// </summary>
    /// <remarks>
    /// It is on the answer because a reader otherwise cannot tell a group of one
    /// - which this shape never returns - from a group of many. It is a count
    /// and never a list.
    /// </remarks>
    public long AccountsFolded { get; }

    /// <summary>
    /// Folds one year's plays into a leaderboard, or withholds it.
    /// </summary>
    /// <param name="plays">The year's plays, any account. The year is chosen before they get here.</param>
    /// <param name="hasAgreed">Whether an account has consent recorded, asked once per account with a play.</param>
    /// <param name="fewestAccountsBehindARow">How many accounts have to stand behind the combined group before it may be answered.</param>
    /// <returns>The leaderboard, or null where answering it would name whoever did not agree.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The threshold is not a positive number.</exception>
    public static ConsentedLeaderboard? Over(
        IEnumerable<PlayRecord> plays,
        Func<Guid, bool> hasAgreed,
        int fewestAccountsBehindARow)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(hasAgreed);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fewestAccountsBehindARow);

        var named = new Dictionary<Guid, Tally>();
        var folded = new HashSet<Guid>();
        long foldedPlays = 0;
        var foldedWatched = TimeSpan.Zero;

        // The register is asked once per account rather than once per play, and
        // the answer is remembered only for the length of this fold. Asking per
        // play would multiply the reads by however much somebody watched, and
        // remembering it past the fold is the cache issue #42's second
        // condition refuses by name.
        var agreed = new Dictionary<Guid, bool>();

        foreach (var play in plays)
        {
            if (!agreed.TryGetValue(play.UserId, out var mayBeNamed))
            {
                mayBeNamed = hasAgreed(play.UserId);
                agreed[play.UserId] = mayBeNamed;
            }

            if (mayBeNamed)
            {
                if (!named.TryGetValue(play.UserId, out var theirs))
                {
                    theirs = new Tally();
                    named[play.UserId] = theirs;
                }

                theirs.Add(play.WatchedDuration);
                continue;
            }

            folded.Add(play.UserId);
            foldedPlays++;
            foldedWatched += play.WatchedDuration;
        }

        if (folded.Count > 0 && folded.Count < fewestAccountsBehindARow)
        {
            return null;
        }

        var rows = named
            .Select(entry => new LeaderboardRow(entry.Key, entry.Value.Plays, entry.Value.Watched))
            .OrderByDescending(row => row.Watched)
            .ThenByDescending(row => row.Plays)
            .ThenBy(row => row.UserId!.Value)
            .ToList();

        if (folded.Count > 0)
        {
            rows.Add(new LeaderboardRow(null, foldedPlays, foldedWatched));
        }

        return new ConsentedLeaderboard(rows, folded.Count);
    }

    private sealed class Tally
    {
        public long Plays { get; private set; }

        public TimeSpan Watched { get; private set; }

        public void Add(TimeSpan watched)
        {
            Plays++;
            Watched += watched;
        }
    }
}
