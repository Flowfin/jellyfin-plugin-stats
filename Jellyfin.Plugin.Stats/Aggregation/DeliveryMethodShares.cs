using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How a set of plays divides between the ways the server delivered them.
/// </summary>
/// <remarks>
/// Four numbers and not three. A play the server never reported a method for is
/// reported as its own figure and is never counted as direct, which was decided
/// on issue #53 on 2026-08-09: counting missing information as the good outcome
/// makes a chart where an absent answer looks like success, and an
/// administrator reads a server that is behaving worse than the picture says.
/// <para>
/// What it folds is the method the row carries, which the server reported when
/// the play started, and not the transcoding summary folded over the whole
/// play. That is the same decision. The two can disagree, and where they do it
/// is a finding of its own rather than something a share quietly picks a side
/// on; issue #158 holds that disagreement.
/// </para>
/// <para>
/// The play count is counted here rather than worked out by adding the four up,
/// so that the two are separate statements and a fold that lost a row would
/// show. Adding them up and calling that the total would make the property this
/// type exists to hold true by definition, which is the arithmetic equivalent
/// of a test that asserts what it just assigned.
/// </para>
/// <para>
/// Nothing here divides. A percentage over a range with no plays in it has no
/// answer, and a report that shows nought per cent for every method on an empty
/// range is stating something the rows do not say. Counts are what is folded
/// and whoever draws the chart decides what an empty range looks like, the same
/// way the page module writes a value it does not know as null rather than as
/// zero.
/// </para>
/// </remarks>
public sealed record DeliveryMethodShares
{
    private DeliveryMethodShares(long unknown, long directPlay, long directStream, long transcode, long plays)
    {
        Unknown = unknown;
        DirectPlay = directPlay;
        DirectStream = directStream;
        Transcode = transcode;
        Plays = plays;
    }

    /// <summary>
    /// Gets the plays the server never reported a delivery method for, and the
    /// plays whose stored method this build has no name for.
    /// </summary>
    public long Unknown { get; }

    /// <summary>
    /// Gets the plays whose file was sent as it is on disk.
    /// </summary>
    public long DirectPlay { get; }

    /// <summary>
    /// Gets the plays whose streams were repackaged without being re-encoded.
    /// This is the figure the reports call remuxed.
    /// </summary>
    public long DirectStream { get; }

    /// <summary>
    /// Gets the plays where at least one stream was re-encoded.
    /// </summary>
    public long Transcode { get; }

    /// <summary>
    /// Gets how many plays were folded, counted as they arrived. The four
    /// figures above add up to this, and that they do is the property the fold
    /// is held to rather than a fact about how it is written.
    /// </summary>
    public long Plays { get; }

    /// <summary>
    /// Folds a sequence of plays into the four figures.
    /// </summary>
    /// <remarks>
    /// A stored row outlives the assembly that wrote it, which is why the
    /// method it carries is this plugin's own closed set rather than the
    /// server's enumeration. A row written by a later build can still carry a
    /// value this one has no name for, and it is counted as unknown rather than
    /// dropped: the plugin does not know how that play was delivered, which is
    /// what unknown means, and dropping it would take a play out of the answer
    /// without anything saying so. It is never counted as direct, for the same
    /// reason the reported absence is not.
    /// </remarks>
    /// <param name="plays">The plays to fold. The range they belong to is chosen before they get here.</param>
    /// <returns>The four figures and the number of plays they were folded from.</returns>
    public static DeliveryMethodShares Over(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        long unknown = 0;
        long directPlay = 0;
        long directStream = 0;
        long transcode = 0;
        long counted = 0;

        foreach (var play in plays)
        {
            counted++;

            switch (play.PlayMethodAtStart)
            {
                case PlayMethod.DirectPlay:
                    directPlay++;
                    break;
                case PlayMethod.DirectStream:
                    directStream++;
                    break;
                case PlayMethod.Transcode:
                    transcode++;
                    break;
                default:
                    unknown++;
                    break;
            }
        }

        return new DeliveryMethodShares(unknown, directPlay, directStream, transcode, counted);
    }
}
