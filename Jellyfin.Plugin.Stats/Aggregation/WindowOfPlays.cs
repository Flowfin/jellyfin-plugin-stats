using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// What one bounded read of play rows came back with: the rows, or the reason
/// there are too many of them to read.
/// </summary>
/// <remarks>
/// A refusal is a value here rather than an exception, because the fold above
/// this has to carry on when one window refuses. A year is a range the caller
/// cannot shorten, so a wrap-up that stopped at the first refused month would
/// hand a heavy watcher a permanent refusal of their own history; what happens
/// instead is that the figures that window would have fed are reported as not
/// computed and the rest of the year stands. Issue #66.
/// <para>
/// It also keeps the fold from naming the query layer. The bound and the
/// exception that expresses it belong to the reports, the fold belongs to the
/// aggregation, and a shape in between is what lets each read the other's answer
/// without depending on the other's types.
/// </para>
/// </remarks>
public sealed record WindowOfPlays
{
    /// <summary>
    /// Gets the rows the window holds. It is empty rather than absent where the
    /// window holds more than the bound allows, so a reader that forgot to look
    /// at <see cref="OverTheBound"/> reads no rows rather than reaching into
    /// nothing, and there is one way to ask whether the window was read.
    /// </summary>
    public IReadOnlyList<PlayRecord> Plays { get; init; } = Array.Empty<PlayRecord>();

    /// <summary>
    /// Gets why the window could not be read, where it could not. It is written
    /// into the answer beside the figures it cost, so a reader meets the reason
    /// rather than a figure that is simply absent.
    /// </summary>
    public string? OverTheBound { get; init; }

    /// <summary>
    /// A window that was read.
    /// </summary>
    /// <param name="plays">What it held.</param>
    /// <returns>The window.</returns>
    public static WindowOfPlays Holding(IReadOnlyList<PlayRecord> plays) => new() { Plays = plays };

    /// <summary>
    /// A window that holds more rows than a read may hold.
    /// </summary>
    /// <param name="because">Why, in the words a reader is given.</param>
    /// <returns>The refusal.</returns>
    public static WindowOfPlays TooManyToRead(string because) => new() { OverTheBound = because };
}
