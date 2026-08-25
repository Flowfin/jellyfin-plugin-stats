namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One hardware acceleration the server reported, how many plays it covered,
/// and how much of them was watched.
/// </summary>
/// <remarks>
/// This is a partition and the reason rows beside it are not, which is the
/// whole of why the two are separate types. A play carries every reason the
/// server gave for it and exactly one acceleration, so these rows add up to the
/// plays they came from and those rows do not.
/// </remarks>
/// <param name="Type">
/// The acceleration, spelled as the server reported it, and null where the
/// server reported none. Null rather than a word: an installation genuinely
/// reporting "none" is a real answer, and a made-up label could not be told
/// from it. What null covers is wider than software transcoding, and the fold
/// cannot narrow it: a play the server passed through untouched reports no
/// acceleration for the same reason a play re-encoded on the processor does.
/// Whoever draws the row says so rather than calling the group software.
/// </param>
/// <param name="Plays">How many plays the server reported it for.</param>
/// <param name="WatchedMinutes">
/// How much of those plays was watched, in minutes. Nothing is counted twice
/// here, unlike the reason rows, because a play has one acceleration.
/// </param>
public sealed record TranscodeAccelerationCount(string? Type, long Plays, double WatchedMinutes);
