// The near miss for no-configuration-value-in-a-static-field.
//
// This is the gate issue #39 asks for, the one thing that stands between a play
// and the store, written the way it is natural to write it. It is correct in
// every other respect. It takes no clock, it names nobody in its log, it holds
// no path and no address, and it answers in one place so no caller has to
// remember to ask.
//
// It is wrong because of where the answers are kept. Both fields are read once,
// when the type is first touched, which on a running server is somewhere in the
// first play after start-up. An administrator who turns capture off then
// watches it go on recording, and an operator who adds themselves to the
// exclusion list stays in the data. Nothing tells either of them; the page
// saved cleanly and the value in the file is the one they typed.
//
// The one word version of this mistake is static. The same two lines on
// instance fields, on a gate the container hands out, read the configuration
// the server holds now rather than the copy this class took, and the two sit
// one keyword apart in a diff.
namespace Jellyfin.Plugin.Stats.NearMiss;

using System;
using System.Linq;
using Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Decides whether a finished play is kept.
/// </summary>
internal static class CaptureGate
{
    private static readonly bool Enabled = Plugin.Instance!.Configuration.CaptureEnabled;

    private static readonly string[] Excluded = Plugin.Instance!.Configuration.ExcludedUserIds;

    /// <summary>
    /// Says whether a play by this user is written.
    /// </summary>
    /// <param name="userId">The user the play belongs to.</param>
    /// <returns>True where the play is kept.</returns>
    public static bool Keeps(Guid userId)
    {
        if (!Enabled)
        {
            return false;
        }

        return !Excluded.Any(entry => Guid.TryParse(entry, out var excluded) && excluded == userId);
    }
}
