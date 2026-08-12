namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One reason and how many plays recorded it.
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
public sealed record TranscodeReasonCount(string Reason, long Plays);
