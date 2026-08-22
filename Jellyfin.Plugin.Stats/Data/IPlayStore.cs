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
/// The reads over rows that carry no bound are the exception that proves it,
/// and they are shaped so a report cannot use them by accident. Each hands back
/// a sequence that is walked once, drawn from the store a row at a time, so a
/// caller holding a year of plays is a caller that chose to; none of them
/// returns a list. They exist for the export in issue #33, which is the one
/// operation whose whole job is every row.
/// </para>
/// <para>
/// One read is unbounded and returns a list, and it is not a read over rows:
/// <see cref="UserIdsWithPlays"/> answers with one entry per account rather
/// than one per play. Its own remarks carry why that is a different statement
/// from the paragraph above rather than an exception to it.
/// </para>
/// <para>
/// The rest carry no bound and are not reads over rows either, because the
/// store reduces the column rather than handing the rows back:
/// <see cref="CountPlaysStartedBefore"/> answers with one number,
/// <see cref="OldestPlayStartedUtc"/> with one moment, and
/// <see cref="YearsWithPlaysFor"/> with one entry per year an account watched
/// anything in, however many rows any of them read. A bound on an answer that
/// is one value however large the table is would be a bound on nothing.
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
    /// Reads back each user identifier the store holds rows for, once.
    /// </summary>
    /// <remarks>
    /// The read that carries no bound and is not a walk, and what makes that
    /// safe is the shape of the answer rather than a caller's restraint: it is
    /// one entry per account that has ever finished a play, so it grows with
    /// the number of people on the server and not with how long the server has
    /// been recording. A store holding a million rows for a household answers
    /// this with four.
    /// <para>
    /// It is the direction <see cref="PlaysFor"/> does not go. That one answers
    /// for an identifier the caller already holds; this is for the caller that
    /// has none, which is the reconciliation asking the server about every
    /// account the store still carries rows for. Deriving the same set by
    /// walking <see cref="AllPlays"/> would read every row in the file to
    /// answer a question one statement answers, on a schedule, forever.
    /// </para>
    /// </remarks>
    /// <returns>Each identifier once, in a stable order, and empty where there are no rows.</returns>
    IReadOnlyList<Guid> UserIdsWithPlays();

    /// <summary>
    /// Reads the moment the oldest play in the store started, and nothing
    /// where the store holds no rows.
    /// </summary>
    /// <remarks>
    /// How far back the plugin can answer for. An administrator reading a
    /// report over a year on a store that holds ninety days of rows is reading
    /// ninety days under a yearly heading, and this is the one figure that says
    /// so. Issue #65 is where it is shown.
    /// <para>
    /// Absent is a value of its own rather than a date standing in for one. A
    /// store with no rows and a store whose oldest row is the first moment a
    /// clock can name are different facts, and a caller handed a sentinel has
    /// already lost the difference before it can draw anything.
    /// </para>
    /// <para>
    /// The moment is when the play started and not when its row was written.
    /// The two part company in both directions: the retention sweep deletes by
    /// the started column, so the earliest start is the edge of the window a
    /// sweep leaves behind, and an import writes rows in whatever order the file
    /// it read them from held them. A store answering by write order would tell
    /// an administrator who has just imported a year that the plugin knows
    /// nothing older than this afternoon.
    /// </para>
    /// </remarks>
    /// <returns>When the oldest play started, in UTC, and null where the store holds no rows.</returns>
    DateTime? OldestPlayStartedUtc();

    /// <summary>
    /// Reads back each calendar year one account has plays in, once, oldest
    /// first.
    /// </summary>
    /// <remarks>
    /// What a wrap-up's year selector may offer. Issue #67 asks it to list only
    /// years with data, and the shape that mistake arrives in is a list derived
    /// from the oldest row and the year the server is in: that answers which
    /// years a store could have rows in, so a quiet year in the middle of the
    /// span is offered and opens empty. This answers which years it does have
    /// rows in, and a year with none is not in it.
    /// <para>
    /// The zone is part of the question rather than something the caller
    /// applies afterwards. A calendar year has a local midnight at each end, so
    /// a play in the last hours of December belongs to one year or the next
    /// depending on whose midnight is meant, and a store answering in UTC would
    /// be answering a question nobody asked. It is the only read here that
    /// names one.
    /// </para>
    /// <para>
    /// It carries no bound, and the reason is the one
    /// <see cref="UserIdsWithPlays"/> carries rather than an exception beside
    /// it: the answer is one entry per year an account has watched anything in,
    /// so it grows with how long that account has been on the server and not
    /// with how much they watched. A million rows over three years answer with
    /// three numbers.
    /// </para>
    /// <para>
    /// It is read rather than kept, for the same reason the oldest row is. The
    /// retention sweep deletes by the started column, so a year leaves this list
    /// when the last of its rows goes, and a selector drawn from a value taken
    /// at start-up would go on offering a year whose rows are gone from the
    /// file.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account whose years are wanted.</param>
    /// <param name="zone">The zone the years are read in, which is what decides where one ends and the next begins.</param>
    /// <returns>Each year once, ascending, and empty where that account has no rows.</returns>
    IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone);

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
    /// Deletes rows belonging to one user that started inside a window, up to a
    /// limit, oldest written first.
    /// </summary>
    /// <remarks>
    /// A third deletion rather than a window argument on the one above, because
    /// a bound that is sometimes there is a condition assembled in C# and
    /// pasted into a statement, which is the shape
    /// <c>no-sql-built-by-concatenation</c> refuses. Each of the three is one
    /// statement whose text never moves.
    /// <para>
    /// The window is half open: a row starting exactly at
    /// <paramref name="fromUtc"/> goes and a row starting exactly at
    /// <paramref name="toUtc"/> stays. Two windows laid end to end therefore
    /// delete each row once, which a closed window would not, and a caller
    /// deleting a calendar month names the first instant of the next one rather
    /// than a tick before it.
    /// </para>
    /// <para>
    /// Both bounds are refused unless they say they are in UTC, for the reason
    /// the sweep's cutoff is: a local moment read as UTC moves the boundary by
    /// the machine's offset, and on a deletion that is rows nobody asked to
    /// lose and rows somebody asked to lose and still has.
    /// </para>
    /// </remarks>
    /// <param name="userId">The user whose rows go.</param>
    /// <param name="fromUtc">The first moment in the window, in UTC.</param>
    /// <param name="toUtc">The first moment after the window, in UTC.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit);

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
