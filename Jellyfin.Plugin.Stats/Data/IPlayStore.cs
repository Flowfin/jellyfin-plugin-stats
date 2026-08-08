using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Where finished plays are kept, and the only way the rest of the plugin
/// reaches them.
/// </summary>
/// <remarks>
/// Nothing in these signatures names a storage technology, a connection, a
/// statement or a file. Which store this is was the first question in issue #10
/// and the answer was a SQLite file of the plugin's own; the point of this
/// interface is that revisiting that answer is a change to one class rather
/// than to every caller.
/// <para>
/// The read a report is built on is bounded by an argument rather than by the
/// caller remembering to stop. A store that grows for years has no safe
/// unbounded read, and a method that offers one is the method somebody calls
/// from a report.
/// </para>
/// <para>
/// The two reads below that carry no bound are the exception that proves it,
/// and they are shaped so a report cannot use them by accident. Both hand back
/// a sequence that is walked once, drawn from the store a row at a time, so a
/// caller holding a year of plays is a caller that chose to; neither returns a
/// list. They exist for the export in issue #33, which is the one operation
/// whose whole job is every row.
/// </para>
/// </remarks>
public interface IPlayStore : IDisposable
{
    /// <summary>
    /// Adds one finished play.
    /// </summary>
    /// <param name="play">The play to keep.</param>
    void Add(PlayRecord play);

    /// <summary>
    /// Reads back the most recently started plays, newest first.
    /// </summary>
    /// <param name="limit">How many rows at most. The store never returns more than this.</param>
    /// <returns>The rows, newest first, and empty where there are none.</returns>
    IReadOnlyList<PlayRecord> MostRecentPlays(int limit);

    /// <summary>
    /// Walks every row in the store, oldest written first.
    /// </summary>
    /// <remarks>
    /// The order is the order the rows were written and not the order they were
    /// played, because an export is compared against another export and a
    /// tie-break on a timestamp two rows share is not one.
    /// </remarks>
    /// <returns>Every row, walked once.</returns>
    IEnumerable<PlayRecord> AllPlays();

    /// <summary>
    /// Walks every row belonging to one user, in the same order.
    /// </summary>
    /// <remarks>
    /// The store answers this with one statement rather than the caller reading
    /// every row and discarding most of them, which is the difference between
    /// an export a user can ask for and one that reads the whole server to
    /// answer it.
    /// </remarks>
    /// <param name="userId">The user whose rows are wanted.</param>
    /// <returns>That user's rows, walked once, and empty where there are none.</returns>
    IEnumerable<PlayRecord> PlaysFor(Guid userId);
}
