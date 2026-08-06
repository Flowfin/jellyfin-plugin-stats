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
/// The read is bounded by an argument rather than by the caller remembering to
/// stop. A store that grows for years has no safe unbounded read, and a method
/// that offers one is the method somebody calls from a report.
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
}
