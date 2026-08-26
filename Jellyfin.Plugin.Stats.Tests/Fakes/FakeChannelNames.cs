// What a channel identifier is called, without a library to ask. A test that
// drives a live television play says here what the channel was called, and a
// test about anything else takes the empty one, where nothing resolves.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Capture;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// A stand-in for the server's library, holding the channels a test declared.
/// </summary>
public sealed class FakeChannelNames : IChannelNames
{
    private readonly Dictionary<Guid, string> _named = [];

    /// <summary>
    /// Gets a set of channels nobody named, which is what a play of anything
    /// other than live television resolves against.
    /// </summary>
    public static FakeChannelNames Empty => new();

    /// <summary>
    /// Gets how many times a name was asked for.
    /// </summary>
    /// <remarks>
    /// A count rather than a flag, because what a reader of the capture wants
    /// to know is that the lookup happens once for a play that reports a
    /// hundred times, and a flag cannot say that.
    /// </remarks>
    public int TimesAsked { get; private set; }

    /// <summary>
    /// Declares what a channel is called.
    /// </summary>
    /// <param name="channelId">The channel.</param>
    /// <param name="name">Its name.</param>
    /// <returns>This set.</returns>
    public FakeChannelNames Called(Guid channelId, string name)
    {
        _named[channelId] = name;
        return this;
    }

    /// <inheritdoc />
    public string? NameOf(Guid channelId)
    {
        TimesAsked++;

        return _named.TryGetValue(channelId, out var name) ? name : null;
    }
}
