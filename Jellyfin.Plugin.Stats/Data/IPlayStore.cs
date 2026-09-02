using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Where plays are kept, both the ones that have finished and the ones that are
/// still running, and the only way the rest of the plugin reaches them.
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
/// Every removal here names what it means by removing, as an argument rather
/// than as something the store works out. A retention sweep that takes a
/// deleted account's last rows is still retention and a person deleting a
/// fortnight inside the retention window is still correcting the record, so
/// neither the rows that went nor the caller that asked says which of the two
/// it was. Issue #251, and <see cref="DeletionsRecorded"/> is where a later
/// reader meets the answer.
/// </para>
/// <para>
/// Every removal here reaches the open plays as well as the finished ones, and
/// that is the store's own doing rather than something each caller remembers.
/// An open row holds the same account and the same item name a finished row
/// does, so a deletion that took one and left the other would answer a request
/// to be forgotten with the rows still there. The set of removals is small and
/// closed; the set of reads is not, which is why the covering is on this side.
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
    /// Gets the zone the day-by-day rollups in this store were counted in, or
    /// null where the store has not keyed one yet.
    /// </summary>
    /// <remarks>
    /// Which day a play falls on is not a fact about the play, so a day in a
    /// rollup means nothing without this. It is stated once for the whole table
    /// rather than carried on each row, because a file holding two answers for
    /// one day, keyed in two zones, is a file nothing can read as a day.
    /// <para>
    /// Null says the store has never keyed a rollup. It is not the same as the
    /// zone being the default one, and a reader that treats it as such would
    /// report a day boundary the store has never used.
    /// </para>
    /// <para>
    /// A STORE STATES THE ZONE IT WAS FIRST KEYED IN AND NOT THE ONE THE PROCESS
    /// IS CONFIGURED WITH TODAY. A store opened under a different setting keeps
    /// the zone already stated, because rekeying every rollup is a rebuild and
    /// issue #253 is where that lives. Until then a changed setting reaches
    /// everything a report folds out of play rows and reaches none of these
    /// rows, and this property is how a reader tells which of the two they are
    /// looking at.
    /// </para>
    /// </remarks>
    TimeZoneInfo? RollupZone { get; }

    /// <summary>
    /// Adds one finished play.
    /// </summary>
    /// <param name="play">The play to keep.</param>
    void Add(PlayRecord play);

    /// <summary>
    /// Adds a sequence of finished plays, as one piece of work rather than as
    /// one piece of work each.
    /// </summary>
    /// <remarks>
    /// The default is the loop a caller would otherwise write, so an
    /// implementation that has nothing to gain from the difference owes
    /// nothing. What the store on a file gains is the whole of why this exists:
    /// a row written on its own is its own transaction and its own flush to the
    /// disk, and what that rate comes to was measured rather than reasoned
    /// about, under issue #56, where the figures are. A store that can only be
    /// written a row at a time is a store nobody can fill, and a report whose
    /// behaviour over a large one is therefore never exercised is a report
    /// nobody has measured.
    /// <para>
    /// IT IS ALL OR NOTHING WHERE THE IMPLEMENTATION MAKES IT SO, AND THAT IS A
    /// DIFFERENCE RATHER THAN A DETAIL. <see cref="Add"/> called in a loop
    /// leaves the rows before a failure behind it. A caller that needs the rows
    /// before a bad one kept - which is what an archive import is written to do
    /// - calls <see cref="Add"/> and not this.
    /// </para>
    /// </remarks>
    /// <param name="plays">The plays to keep.</param>
    void AddMany(IEnumerable<PlayRecord> plays)
    {
        ArgumentNullException.ThrowIfNull(plays);

        foreach (var play in plays)
        {
            Add(play);
        }
    }

    /// <summary>
    /// Writes a play that has started and not stopped, replacing what was
    /// written for the same key.
    /// </summary>
    /// <remarks>
    /// The key is the row's identity, so a play reported a thousand times is
    /// one row and a play reported twice is one row. That is what stops the
    /// file growing with how often a session checks in, and it is the reason
    /// this is a write rather than an append. Issue #220.
    /// <para>
    /// It is a second table and not a flag on the finished rows. Every read in
    /// this plugin answers a question about plays that happened, and a running
    /// play in that table makes every one of those reads wrong unless it
    /// remembers a condition nobody would notice missing.
    /// </para>
    /// </remarks>
    /// <param name="play">The play as it stands.</param>
    void NoteOpenPlay(OpenPlay play);

    /// <summary>
    /// Adds one finished play and takes away the open row it came from, both
    /// or neither.
    /// </summary>
    /// <remarks>
    /// One transaction, because the two halves apart are how a play appears
    /// twice. A process that died between them would leave the finished row
    /// beside an open row for the same play, and whatever finishes what a
    /// restart left open would then write the play a second time. Appearing
    /// exactly once is the property the three pieces of issue #36 share, and
    /// this is where it is kept.
    /// <para>
    /// A key with no open row against it is not a failure. A play whose start
    /// this plugin never saw, and a play whose open row a sweep has already
    /// taken, both arrive here with nothing to remove, and the finished row is
    /// still the answer.
    /// </para>
    /// </remarks>
    /// <param name="play">The finished play.</param>
    /// <param name="playKey">The key the open row was written under.</param>
    void AddAndForgetOpenPlay(PlayRecord play, string playKey);

    /// <summary>
    /// Takes away the open row for one key, without writing a finished play.
    /// </summary>
    /// <remarks>
    /// What a play that is no longer being recorded leaves behind. Capture
    /// turned off, or a user excluded, part of the way through a play means the
    /// finished row is refused, and an open row already on the file would
    /// otherwise stay there for a play nobody may keep.
    /// </remarks>
    /// <param name="playKey">The key the open row was written under.</param>
    void ForgetOpenPlay(string playKey);

    /// <summary>
    /// Reads every play the file holds as still running.
    /// </summary>
    /// <remarks>
    /// What a previous process left behind, for whatever finishes it. The
    /// sequence is walked once and drawn a row at a time, which is the same
    /// justification the export's reads carry: a caller holding all of them is
    /// a caller that chose to. In practice it is one row per session that was
    /// playing when the server stopped.
    /// <para>
    /// A row in here is not a play that happened. It is what the server had
    /// said about a play up to the last time it heard from the session, and
    /// nothing in it says the play ended.
    /// </para>
    /// </remarks>
    /// <returns>The open plays, in key order.</returns>
    IEnumerable<OpenPlay> OpenPlays();

    /// <summary>
    /// Reads back the most recently started plays, newest first.
    /// </summary>
    /// <param name="limit">How many rows at most. The store never returns more than this.</param>
    /// <returns>The rows, newest first, and empty where there are none.</returns>
    IReadOnlyList<PlayRecord> MostRecentPlays(int limit);

    /// <summary>
    /// Reads the plays that started inside a window, oldest first, up to a
    /// limit.
    /// </summary>
    /// <remarks>
    /// The read every aggregate report is answered from, and the one read here
    /// that carries both a range and a bound. A report asks a question about a
    /// period, so answering it by walking the whole file and discarding most of
    /// it would be a read whose cost grows with how long the server has been
    /// recording rather than with the period asked about.
    /// <para>
    /// The window is half open: a play starting exactly at
    /// <paramref name="fromUtc"/> is in it and one starting exactly at
    /// <paramref name="toUtc"/> is not. Two windows laid end to end therefore
    /// read each play once, which a closed window would not, and a caller asking
    /// for a calendar month names the first instant of the next one rather than
    /// a tick before it. Both bounds are refused unless they say they are in
    /// UTC, for the reason the deletions carry.
    /// </para>
    /// <para>
    /// It returns a list rather than a walk, unlike the reads above it, and the
    /// bound is what makes that safe: the caller has said how much it will hold
    /// before the statement runs. An answer that reached the limit is not marked
    /// as having reached it, which is a gap rather than something decided
    /// quietly, and issue #56 is where an honest answer to a range too large to
    /// fold belongs.
    /// </para>
    /// </remarks>
    /// <param name="fromUtc">The first moment in the window, in UTC.</param>
    /// <param name="toUtc">The first moment after the window, in UTC.</param>
    /// <param name="limit">How many rows at most. The store never returns more than this.</param>
    /// <returns>The plays, oldest first, and empty where there are none.</returns>
    IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit);

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
    /// Walks every day-by-day rollup the store holds.
    /// </summary>
    /// <remarks>
    /// A sequence walked once and drawn a row at a time, the same shape and for
    /// the same reason as the exports above: a caller holding every rollup on a
    /// server is a caller that chose to. What a report wants is a range, and
    /// that is issue #254 rather than this.
    /// </remarks>
    /// <returns>Every rollup, walked once, in day order.</returns>
    IEnumerable<DailyRollup> AllRollups();

    /// <summary>
    /// Reads the day-by-day rollups one account has inside a range of days.
    /// </summary>
    /// <remarks>
    /// What a report over a year issues, and the reason the table exists. The
    /// walk above hands back every rollup on the server, so reading one year
    /// through it would touch every day of every account to answer about one
    /// account's twelve months, which is the scan the fold on the write path was
    /// built to stop. Issue #254.
    /// <para>
    /// Bounded like every other read here, and the bound is the caller's to
    /// decide about. The store hands back at most <paramref name="limit"/> rows
    /// and says nothing about whether the range held more, which is the same
    /// shape <see cref="PlaysBetween"/> has and for the same reason: what a
    /// short answer means is a question about the report, not about the file. A
    /// caller that must not fold a truncated year asks for one more row than it
    /// will accept and refuses on the extra one, which is what the query layer
    /// already does for plays.
    /// </para>
    /// <para>
    /// The range is half-open, the same way a window over plays is: the first
    /// day is in and the last is the first day after. A calendar year is then
    /// the first of January to the first of January, and no caller has to know
    /// whether the year it asked about had a leap day in it.
    /// </para>
    /// <para>
    /// The days are the local days the store states its rollups are counted in,
    /// which is <see cref="RollupZone"/> and is not a fact about any play. A
    /// caller working a year out from a zone of its own would be asking about
    /// days this table does not hold.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <param name="fromDay">The first day in the range.</param>
    /// <param name="toDay">The first day after the range.</param>
    /// <param name="limit">How many rows at most. The store never returns more than this.</param>
    /// <returns>The rollups, in day order, and empty where there are none.</returns>
    IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit);

    /// <summary>
    /// Throws away every rollup the store holds and folds them again from the
    /// play rows.
    /// </summary>
    /// <remarks>
    /// What makes a rollup derived rather than authoritative. A table that
    /// cannot be produced again from the rows underneath it is the only copy of
    /// what it holds, and a table that has drifted from those rows is worse than
    /// no table at all, because it is believed. Both are read the same way:
    /// rebuild and compare. Issue #253.
    /// <para>
    /// It reads the rows a page at a time rather than all of them, for the
    /// reason every other read here carries a bound: a server that has been
    /// recording for years has no safe unbounded read, and a rebuild is exactly
    /// the operation somebody runs against the largest store they have.
    /// </para>
    /// <para>
    /// The result is the incremental fold's, not an approximation of it. Every
    /// column of a rollup is one a play row carries or one that follows from the
    /// play rows alone, so the two agree exactly, and a case that finds them
    /// disagreeing has found a defect in one of them rather than a rounding.
    /// </para>
    /// <para>
    /// What it cannot recover is a play whose row is gone. A retention deletion
    /// removes rows and leaves the figures over them standing, so a rebuild on a
    /// swept store produces the days it still has rows for and not the days it
    /// has aged out of. That is the setting doing its work rather than a defect,
    /// and it is why a rebuild is asked for by an operator rather than run on a
    /// schedule.
    /// </para>
    /// </remarks>
    void RebuildRollups();

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
    /// <param name="deletionClass">What this deletion says about the rows it removes, recorded beside it.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit);

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
    /// <param name="deletionClass">What this deletion says about the rows it removes, recorded beside it.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many finished rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit);

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
    /// <param name="deletionClass">What this deletion says about the rows it removes, recorded beside it.</param>
    /// <param name="limit">How many rows at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rows this call deleted, and zero where there were none left to delete.</returns>
    int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit);

    /// <summary>
    /// Counts the day-by-day rollups keyed to a day before a given one.
    /// </summary>
    /// <remarks>
    /// The aggregate half of what a sweep asks before it starts deleting, so it
    /// can say how far through it is. It carries no bound because its answer is
    /// one number however many rows it counted.
    /// <para>
    /// The day is a day in <see cref="RollupZone"/> and not a moment. A rollup
    /// is keyed to a calendar day, so a caller holding an instant converts it
    /// through that zone rather than through the machine's, and a store that
    /// has keyed no rollup states no zone for one to convert through.
    /// </para>
    /// </remarks>
    /// <param name="day">The first day that is kept. A rollup keyed before it is counted.</param>
    /// <returns>How many rollups are keyed before that day.</returns>
    long CountRollupsBefore(DateOnly day);

    /// <summary>
    /// Deletes the day-by-day rollups keyed to a day before a given one, up to
    /// a limit.
    /// </summary>
    /// <remarks>
    /// The aggregate window, which is a second window rather than a second
    /// meaning of the first: the raw rows and the figures folded from them are
    /// kept for different lengths of time, and this is the deletion the longer
    /// of the two performs.
    /// <para>
    /// The limit is here for the reason it is on the play deletions. One
    /// statement removing a decade of rollups holds the write lock for its whole
    /// duration and answers no cancellation in the middle of it, so the caller
    /// takes a bite at a time and decides between bites whether to take another.
    /// </para>
    /// <para>
    /// A rollup that goes is gone. Where the rows it was folded from are still
    /// in the file it can be folded again by <see cref="RebuildRollups"/>, and
    /// where the shorter window has already taken them it cannot: deleting the
    /// rollup then destroys the only remaining record of that day. Which of the
    /// two an installation has chosen follows from its two settings rather than
    /// from anything on the row, so it is said where the settings are typed and
    /// not recorded here.
    /// </para>
    /// </remarks>
    /// <param name="day">The first day that is kept. A rollup keyed before it is deleted.</param>
    /// <param name="deletionClass">What this deletion says about the rollups it removes, recorded beside it.</param>
    /// <param name="limit">How many rollups at most. The store never deletes more than this in one call.</param>
    /// <returns>How many rollups this call deleted, and zero where there were none left to delete.</returns>
    int DeleteRollupsBefore(DateOnly day, DeletionClass deletionClass, int limit);

    /// <summary>
    /// Reads back the deletions this store has performed, newest first.
    /// </summary>
    /// <remarks>
    /// What makes the class something a later reader can see rather than
    /// something the caller knew at the time. Every deletion above removes rows
    /// and leaves nothing behind saying what it meant by removing them, so a
    /// reader arriving afterwards has a gap in the rows and no way to tell a
    /// window that aged out from plays somebody asked to stop counting. This is
    /// where that is written down, and it is written by the store at the moment
    /// it deletes rather than by whoever called.
    /// <para>
    /// A call that removed no rows leaves nothing here. A caller bites until a
    /// bite comes back empty, so the last call of every deletion removes
    /// nothing, and recording those would fill this with entries saying that
    /// nothing happened.
    /// </para>
    /// <para>
    /// Newest first because a reader asking this question is asking about the
    /// deletions since whatever they last read, and a bound taken off the far
    /// end of an oldest-first answer is the wrong end of the table. Issue #251.
    /// </para>
    /// </remarks>
    /// <param name="limit">How many entries at most, newest first.</param>
    /// <returns>What was deleted and what each deletion said about it, newest first, and empty where the store has deleted nothing.</returns>
    IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit);

    /// <summary>
    /// Reads what one account has said about being named, and null where it has
    /// said nothing.
    /// </summary>
    /// <remarks>
    /// Null and a record saying no are different answers and both are needed.
    /// An account that has never been asked has not refused, and a view that
    /// treated the two the same could never tell somebody they had a question
    /// waiting. Issue #42.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <returns>What that account has said, or null where it has said nothing.</returns>
    ConsentRecord? ConsentFor(Guid userId);

    /// <summary>
    /// Reads back each account the store holds a consent record for, once.
    /// </summary>
    /// <remarks>
    /// The consent table's answer to the question <see cref="UserIdsWithPlays"/>
    /// answers over the plays, and it exists because the two sets are not the
    /// same one. An account can hold a record and no plays - it answered the
    /// question and then watched nothing, or its rows aged out under retention -
    /// so a reconciliation walking only the accounts with plays never reaches
    /// it, and the record outlives the account. Issue #296.
    /// <para>
    /// One entry per account, like the read over the plays and for the same
    /// reason: the table is keyed by the account, so it holds one row each and
    /// the answer grows with the people on a server rather than with how long
    /// it has been recording.
    /// </para>
    /// <para>
    /// A list rather than a walk, again for the reason that one carries. The
    /// caller asks the server about every identifier this returns and then
    /// deletes against the same store, and a reader left open over the table
    /// while deletions run against it is a reader whose remaining rows are
    /// whatever the deletions left.
    /// </para>
    /// </remarks>
    /// <returns>Each account holding a record, once, in a stable order, and empty where the store holds none.</returns>
    IReadOnlyList<Guid> UserIdsWithConsent();

    /// <summary>
    /// Writes what one account has said, replacing whatever it said before.
    /// </summary>
    /// <remarks>
    /// The account is the key, because the question has one answer at a time
    /// and what every reader asks is what that answer is now. The record
    /// carries both moments, so replacing it does not lose the agreement a
    /// withdrawal is a withdrawal of.
    /// </remarks>
    /// <param name="consent">What the account has said.</param>
    void RecordConsent(ConsentRecord consent);

    /// <summary>
    /// Takes away what one account said, leaving no record that it was asked.
    /// </summary>
    /// <remarks>
    /// For an account the server no longer has. It is not what a withdrawal
    /// does: withdrawing is an answer and is recorded as one, and an account
    /// that is gone has nobody left to have answered.
    /// <para>
    /// It is separate from the removals over plays, and deliberately. A person
    /// deleting their own history is still on the server and their answer to
    /// this question is still theirs, so a removal that took the two together
    /// would make somebody who cleared their history look like somebody who had
    /// never been asked.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account.</param>
    void ForgetConsentFor(Guid userId);

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
