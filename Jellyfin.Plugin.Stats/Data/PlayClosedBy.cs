namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// What ended a play, out of the routes that can produce a finished row.
/// </summary>
/// <remarks>
/// A closed set of this plugin's own, and a stored value. The numbering outlives
/// the assembly that wrote it, so a member is added at the end and no member's
/// number ever moves, the same rule <see cref="PlayMethod"/> is written under.
/// <para>
/// ISSUE #222 NAMES TWO ROUTES AND THIS TREE HAS FOUR. It was written when a
/// play was closed either by a stop event or by a timeout, and asks for a column
/// recording which of the two. Two more arrived with the work that made the
/// timeout possible at all: a session ending while a play is open, and a process
/// starting up and finishing what the last one left running. All four produce a
/// row that is indistinguishable from the others today, which is the thing that
/// issue is about, so all four are recorded rather than two of them and a
/// bucket. Recording fewer would answer the condition and leave the report
/// unable to say what it is about.
/// </para>
/// <para>
/// What a report calls ending cleanly is <see cref="AStopEvent"/> and nothing
/// else. The other three are a play the server stopped telling this plugin
/// about, and the row's end is the last moment it heard from the session rather
/// than the moment the person stopped watching.
/// </para>
/// </remarks>
public enum PlayClosedBy
{
    /// <summary>
    /// The row does not say.
    /// </summary>
    /// <remarks>
    /// Two rows read as this and they are not the same thing, which is why it is
    /// worded as the row not saying rather than as nothing having closed the
    /// play. A row written by a build from before this column existed does not
    /// say because nothing was recording it. A row in the table of plays that
    /// are still running does not say because it has not been closed at all.
    /// <para>
    /// It is zero, so a column added to a table that already has rows reads back
    /// as this for every one of them, which is issue #222's second condition:
    /// not saying is the honest answer and being counted as closed cleanly is
    /// the wrong one.
    /// </para>
    /// </remarks>
    NotSaid = 0,

    /// <summary>
    /// The server sent a stop for the play.
    /// </summary>
    /// <remarks>
    /// The clean ending, and the only one. The server said the play was over and
    /// said whether the item had been played through, so the row's end is when
    /// the play ended rather than when something gave up waiting for it.
    /// </remarks>
    AStopEvent = 1,

    /// <summary>
    /// The session the play was on ended while the play was still open.
    /// </summary>
    /// <remarks>
    /// A play that will never receive a stop. Nothing here claims the item was
    /// played through, because nothing ever said so.
    /// </remarks>
    TheSessionEnding = 2,

    /// <summary>
    /// The session said nothing for longer than the bound and the play was
    /// closed on that.
    /// </summary>
    /// <remarks>
    /// A client that lost its network, a device switched off, a tab closed hard.
    /// None of those produces an event, so what closed the play is a length of
    /// silence rather than anything the server sent.
    /// </remarks>
    GoingQuiet = 3,

    /// <summary>
    /// A later process found the play still running on the file and finished it.
    /// </summary>
    /// <remarks>
    /// The play belonged to a process that is gone. The row is what that process
    /// last wrote, so its end is the last moment that server heard from the
    /// session, and everything after that moment is lost rather than estimated.
    /// </remarks>
    ARestart = 4
}
