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

    /// <summary>
    /// Counts the rows that started before a moment.
    /// </summary>
    /// <remarks>
    /// This is what a sweep asks before it starts deleting, so it can say how
    /// far through it is. It carries no bound because its answer is one number
    /// however many rows it counted.
    /// </remarks>
    /// <param name="cutoffUtc">The moment, in UTC. A row that started before it is counted.</param>
    /// <returns>How many rows started before that moment.</returns>
    long CountPlaysStartedBefore(DateTime cutoffUtc);

    /// <summary>
    /// Deletes rows that started before a moment, up to a limit, oldest written
    /// first.
    /// </summary>
    /// <remarks>
    /// The limit is what makes a first sweep over years of rows interruptible.
    /// One statement deleting everything holds a write lock for as long as it
    /// takes and answers no cancellation in the middle of it, so the caller
    /// takes a bite at a time and decides between bites whether to take
    /// another.
    /// <para>
    /// The rows do not come back. There is no flag and no second table: a row
    /// past its retention window is gone from the file, which is the whole
    /// point of a retention window.
    /// </para>
    /// </remarks>
    /// <param name="cutoffUtc">The moment, in UTC. A row that started before it is deleted.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit);

    /// <summary>
    /// Deletes rows belonging to one user, up to a limit, oldest written first.
    /// </summary>
    /// <remarks>
    /// A second deletion beside the retention one, because the two are asked
    /// different questions. The sweep deletes by age, for everybody; this
    /// deletes by identifier, whatever the age. Neither can be written in terms
    /// of the other.
    /// <para>
    /// Bounded by an argument for the same reason the sweep's is. A server one
    /// person uses has all of its rows under one identifier, so "one user's
    /// rows" is not a small set by construction, and a single statement over
    /// years of them holds the write lock for its whole duration.
    /// </para>
    /// <para>
    /// The rows do not come back. There is no flag and no second table: the
    /// row is gone from the table, which is what makes this a deletion rather
    /// than a filter a later reader could be asked to skip.
    /// </para>
    /// </remarks>
    /// <param name="userId">The user whose rows go.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysFor(Guid userId, int limit);

    /// <summary>
    /// Gives the space that deleted rows were occupying back to the file
    /// system.
    /// </summary>
    /// <remarks>
    /// A delete does not shrink the file. The pages it frees are kept for the
    /// store's own reuse, so an administrator who set a retention window and
    /// then looked at the folder would see a file exactly as large as before
    /// and conclude that nothing had happened. This is the step that makes the
    /// file smaller, and it is separate from the delete because it is worth
    /// doing once at the end of a sweep rather than after every bite.
    /// </remarks>
    void ReclaimFreedSpace();
}
