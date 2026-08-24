using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How a set of plays divides between the routes that ended them.
/// </summary>
/// <remarks>
/// What issue #222 asks a report to be able to say: of the plays it read, how
/// many ended cleanly. A play the server sent a stop for and a play something
/// gave up waiting for are different facts about the data, and a report that
/// cannot tell them apart is a report whose watched time is right for some of
/// its rows and short for the rest with nothing saying which.
/// <para>
/// FOUR FIGURES AND ONE OF THEM IS AN ABSENCE. A row written before the route
/// was recorded says nothing about how it was closed, and that is its own count
/// rather than being folded into either side. Counting it as clean would claim
/// something the row does not say, in the direction that flatters the server;
/// counting it as unclean would do the same in the other. It is the figure that
/// shrinks as the rows written before that column age out of the retention
/// window, so a reader watching it fall is watching the record improve.
/// </para>
/// <para>
/// The play count is counted here rather than worked out by adding the four up,
/// for the same reason <see cref="DeliveryMethodShares"/> does it: adding them
/// up and calling that the total makes the property this type is held to true by
/// definition, and a fold that lost a row would not show.
/// </para>
/// <para>
/// Nothing here divides. A share over a range with no plays in it has no answer,
/// and whoever draws it decides what an empty range looks like.
/// </para>
/// </remarks>
public sealed record HowPlaysEnded
{
    private HowPlaysEnded(long cleanly, long onASessionEnding, long onSilence, long onARestart, long notSaid, long plays)
    {
        Cleanly = cleanly;
        OnASessionEnding = onASessionEnding;
        OnSilence = onSilence;
        OnARestart = onARestart;
        NotSaid = notSaid;
        Plays = plays;
    }

    /// <summary>
    /// Gets the plays the server sent a stop for.
    /// </summary>
    /// <remarks>
    /// The clean ending, and the only one. The server said the play was over, so
    /// the row's end is when the play ended rather than when something gave up
    /// waiting for it.
    /// </remarks>
    public long Cleanly { get; }

    /// <summary>
    /// Gets the plays closed because the session they were on ended.
    /// </summary>
    public long OnASessionEnding { get; }

    /// <summary>
    /// Gets the plays closed because the session said nothing for longer than
    /// the bound.
    /// </summary>
    public long OnSilence { get; }

    /// <summary>
    /// Gets the plays a later process found still running on the file and
    /// finished.
    /// </summary>
    public long OnARestart { get; }

    /// <summary>
    /// Gets the plays whose row does not say what closed them.
    /// </summary>
    /// <remarks>
    /// Rows written before the route was recorded, and rows carrying a value
    /// this build has no name for, which is what a row from a later build looks
    /// like from here. Both are the plugin not knowing how the play ended, which
    /// is what this figure means, and neither is counted as clean.
    /// </remarks>
    public long NotSaid { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived. The five
    /// figures above add up to this, and that they do is the property this fold
    /// is held to rather than a fact about how it is written.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Folds a sequence of plays into the five figures.
    /// </summary>
    /// <param name="plays">The plays to fold. The range they belong to is chosen before they get here.</param>
    /// <returns>The five figures and the number of plays they were folded from.</returns>
    public static HowPlaysEnded Over(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        long cleanly = 0;
        long onASessionEnding = 0;
        long onSilence = 0;
        long onARestart = 0;
        long notSaid = 0;
        long counted = 0;

        foreach (var play in plays)
        {
            counted++;

            switch (play.ClosedBy)
            {
                case PlayClosedBy.AStopEvent:
                    cleanly++;
                    break;
                case PlayClosedBy.TheSessionEnding:
                    onASessionEnding++;
                    break;
                case PlayClosedBy.GoingQuiet:
                    onSilence++;
                    break;
                case PlayClosedBy.ARestart:
                    onARestart++;
                    break;
                default:
                    notSaid++;
                    break;
            }
        }

        return new HowPlaysEnded(cleanly, onASessionEnding, onSilence, onARestart, notSaid, counted);
    }
}
