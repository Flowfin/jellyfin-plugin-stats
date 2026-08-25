namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One reason, how many plays recorded it, and how much of them was watched.
/// </summary>
/// <remarks>
/// The number is plays and not sightings. A play that reported the same reason
/// on every sample it was watched over is one play here, which is what makes
/// the figure comparable with the play count next to it on the same page.
/// </remarks>
/// <param name="Reason">
/// The reason, spelled as the server reported it. Nothing here tidies, renames
/// or groups the name, because a name the plugin cleaned up no longer matches
/// what an administrator reads in the server's own log.
/// </param>
/// <param name="Plays">How many plays recorded it.</param>
/// <param name="WatchedMinutes">
/// How much of those plays was watched, in minutes, with each play's whole
/// watched time counted under this reason. A play carrying four reasons puts
/// its whole watched time under each of the four rather than a quarter of it
/// under each, which is issue #242: every figure here is a time somebody
/// actually watched under that condition, and a quarter of a play is a number
/// nobody watched. What it costs is that the rows add up to more than the
/// range holds, the same way the play counts do, and the view that draws them
/// says so rather than leaving a reader to work it out.
/// <para>
/// Minutes rather than a duration, for the reason <see cref="WeekCell"/> gives:
/// the drawing scales a number, and a duration reaching a page as text is a
/// number the page has to read back. The sum itself is taken in ticks and
/// converted once at the end, so a row is the same figure however many plays
/// went into it.
/// </para>
/// </param>
public sealed record TranscodeReasonCount(string Reason, long Plays, double WatchedMinutes);
