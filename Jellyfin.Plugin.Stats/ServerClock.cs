using System;

namespace Jellyfin.Plugin.Stats;

/// <summary>
/// The machine clock, in the one file that is allowed to read it.
/// </summary>
/// <remarks>
/// Every other file takes the moment it means as an argument. Capture reads the
/// timestamp the server put on the event, and a day is read in a zone named at
/// the call, so nothing behaves differently on a runner than on a server and a
/// boundary is a value a test can choose.
/// <para>
/// A scheduled task has no such argument. The server hands a scheduled task a
/// progress reporter and a cancellation token and no time at all, and it
/// registers no clock of its own:
/// </para>
/// <para>
/// <c>gh search code --repo jellyfin/jellyfin "TimeProvider"</c> returns
/// nothing on both supported lines, and
/// <c>MediaBrowser.Model/Tasks/IScheduledTask.cs</c> at <c>v10.11.0</c> and at
/// <c>v12.0-rc1</c> declares the same two arguments. So "ninety days ago" has
/// to start somewhere, and it starts here rather than in each consumer.
/// </para>
/// <para>
/// <c>no-ambient-clock</c> in <c>tools/invariants/rules</c> spares this file by
/// name. That is what lets the rule go on refusing every other one: a consumer
/// takes a <see cref="TimeProvider"/> through its constructor, this class is
/// what the container hands it on a server, and a test hands it one that does
/// not move.
/// </para>
/// </remarks>
public static class ServerClock
{
    /// <summary>
    /// Gets the clock of the machine the server runs on.
    /// </summary>
    public static TimeProvider Machine => TimeProvider.System;
}
