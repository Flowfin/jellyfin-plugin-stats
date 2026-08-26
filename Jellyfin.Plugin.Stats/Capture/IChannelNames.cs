using System;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// What a channel identifier was called at the moment a play was recorded.
/// </summary>
/// <remarks>
/// A live television play arrives carrying the identifier of the channel it is
/// on and no name for it: the server fills the name in for one source and live
/// television is not that source, so turning one into the other means resolving
/// a second item through the library.
/// <para>
/// It is a seam rather than a call because it is the one place this plugin's
/// capture path reaches for something other than the event it was handed, and
/// because a test that drives a play has no library to resolve anything
/// through. Issue #40.
/// </para>
/// </remarks>
public interface IChannelNames
{
    /// <summary>
    /// The channel's name, or null where nothing holds that channel any more.
    /// </summary>
    /// <param name="channelId">The channel the play is on.</param>
    /// <returns>The name, or null.</returns>
    string? NameOf(Guid channelId);
}
