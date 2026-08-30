using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One person's own figures over one window, in the shape the page that draws
/// them reads.
/// </summary>
/// <remarks>
/// Every figure here is about the account that asked and about nobody else. It
/// is the counterpart of the aggregate shapes rather than a small version of
/// them: those name no account and are served to an administrator, this one is
/// entirely about one account and is served to that account alone, and the two
/// must never be drawn beside each other - a personal figure next to a server
/// figure is a subtraction with somebody else on the other side of it.
/// <para>
/// A FIGURE THAT COULD NOT BE COMPUTED IS ABSENT AND NAMED IN
/// <see cref="Degraded"/>, NEVER NOUGHT. A nought is a person who watched
/// nothing and an absence is a figure nobody could take, and once one has been
/// written as the other no reader can tell them apart. This is the rule issue
/// #66 settled for a year, and it holds here for the same reason: a window this
/// page offers is not one the reader can shorten. Issue #274.
/// </para>
/// </remarks>
public sealed record OwnFigures
{
    /// <summary>
    /// What a figure folded from the day-by-day rollups is keyed under in
    /// <see cref="Degraded"/>.
    /// </summary>
    public const string PlaysFigure = "plays";

    /// <summary>
    /// What the watched time is keyed under in <see cref="Degraded"/>.
    /// </summary>
    public const string WatchedFigure = "watched";

    /// <summary>
    /// What the finished and abandoned split is keyed under in
    /// <see cref="Degraded"/>. One key for two figures, because they are one
    /// fold and they degrade together or not at all.
    /// </summary>
    public const string CompletionFigure = "completion";

    /// <summary>
    /// What the top items are keyed under in <see cref="Degraded"/>.
    /// </summary>
    public const string TopItemsFigure = "topItems";

    /// <summary>
    /// Gets which window these figures are over, in the words the request named
    /// it with.
    /// </summary>
    /// <remarks>
    /// Echoed back rather than assumed by the reader, so a page cannot draw one
    /// window's figures under another window's heading after a request was
    /// retried.
    /// </remarks>
    public required string Window { get; init; }

    /// <summary>
    /// Gets the zone the window's days were read in.
    /// </summary>
    /// <remarks>
    /// On the answer because a window is unreadable without it: the same thirty
    /// days are two different sets of rows in two zones. It is the zone the
    /// store was keyed in rather than anything the request carried.
    /// </remarks>
    public required string ZoneId { get; init; }

    /// <summary>
    /// Gets how many plays fell in the window, or null where the fold could not
    /// be taken.
    /// </summary>
    public long? Plays { get; init; }

    /// <summary>
    /// Gets how long was watched across them, or null where the fold could not
    /// be taken.
    /// </summary>
    public TimeSpan? Watched { get; init; }

    /// <summary>
    /// Gets how many of those plays reached the end of the item, or null.
    /// </summary>
    public long? Finished { get; init; }

    /// <summary>
    /// Gets how many did not, or null. The two add up to
    /// <see cref="Plays"/> where all three are present, and that they do is
    /// the property this split is held to.
    /// </summary>
    public long? Abandoned { get; init; }

    /// <summary>
    /// Gets the window divided into the parts it is grouped by - one row per day
    /// or per month - or empty where the window is grouped by nothing.
    /// </summary>
    /// <remarks>
    /// Every part of the window is present, including the ones with no rows, so
    /// a quiet Tuesday is a Tuesday reading nought rather than a Tuesday the
    /// series skips. A series that omitted its empty parts would draw a shorter
    /// window than the one that was asked for.
    /// </remarks>
    public IReadOnlyList<UsagePoint> Points { get; init; } = Array.Empty<UsagePoint>();

    /// <summary>
    /// Gets what this account watched most in the window, longest first, or
    /// empty where that figure could not be taken.
    /// </summary>
    /// <remarks>
    /// Empty and absent are told apart by <see cref="Degraded"/> rather than by
    /// the list, because a list cannot carry the difference.
    /// </remarks>
    public IReadOnlyList<TitleRow> TopItems { get; init; } = Array.Empty<TitleRow>();

    /// <summary>
    /// Gets the figures that could not be computed, each against the reason it
    /// could not.
    /// </summary>
    /// <remarks>
    /// Empty where every figure stands. A reader meets the reason beside the
    /// figure it cost rather than a figure that is simply missing, which is what
    /// separates this from an answer somebody has to guess at.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Degraded { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);
}
