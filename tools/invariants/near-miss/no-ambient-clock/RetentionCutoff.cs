// The near miss for no-ambient-clock. Not compiled, not referenced, and here
// only so the rule is proved to bite by a file that fires it.
//
// The mistake is the one somebody actually makes: a sweep needs "ninety days
// ago", the time is right there on the static, and taking it costs nothing at
// the moment it is written. What it costs is later. This method cannot be
// tested for the boundary it exists to compute without waiting for a real
// ninety days to pass, it answers differently on a runner in another zone, and
// the day it deletes is decided by whichever machine happened to run it.
//
// The repair is one parameter. The caller already knows what moment it means,
// because the server put a timestamp on the event or the task framework passed
// one in, and handing it down makes the boundary a value a test can choose.

namespace Jellyfin.Plugin.Stats.NearMiss;

internal static class RetentionCutoff
{
    // What it should have been:
    //
    //     internal static System.DateTimeOffset Before(System.DateTimeOffset now, int days)
    //         => now.AddDays(-days);
    internal static System.DateTimeOffset Before(int days) => System.DateTimeOffset.UtcNow.AddDays(-days);
}
