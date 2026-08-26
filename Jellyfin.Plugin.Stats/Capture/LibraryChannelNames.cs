using System;
using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Resolves a channel's name through the server's library.
/// </summary>
/// <remarks>
/// The whole of this plugin's use of the library, and it is deliberately one
/// question with one answer. The lookup happens once per play, on the start
/// event, so a session reporting every few seconds costs nothing beyond the
/// first, and what it produces is stored on the row rather than asked again
/// when a report is read.
/// <para>
/// A channel the library no longer holds comes back as null, which is the same
/// answer as a play that was not live television at all. The two are told apart
/// by what else is on the row rather than by a third state, because a report
/// can say nothing more about either. Issue #40.
/// </para>
/// <para>
/// What arrives is a function from an identifier to an item rather than the
/// library itself. That holds the reach to its smallest surface twice over: the
/// library is asked for at the moment a channel is, so a server on which
/// nothing live is ever played never reaches for it, and this file names one
/// operation rather than an interface carrying over a hundred. It is also what
/// makes the behaviour below reachable from a suite that has no server, which
/// a stand-in for that interface would not be worth.
/// </para>
/// </remarks>
public sealed class LibraryChannelNames : IChannelNames
{
    private readonly Func<Guid, BaseItem?> _itemInTheLibrary;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryChannelNames"/> class.
    /// </summary>
    /// <param name="itemInTheLibrary">How an identifier becomes the item the library holds for it, or null where it holds none.</param>
    public LibraryChannelNames(Func<Guid, BaseItem?> itemInTheLibrary)
    {
        _itemInTheLibrary = itemInTheLibrary ?? throw new ArgumentNullException(nameof(itemInTheLibrary));
    }

    /// <inheritdoc />
    public string? NameOf(Guid channelId)
    {
        // Nothing is asked for an identifier no play carries. The server fills
        // this in for a live programme and leaves it empty on everything else,
        // and a lookup of an empty identifier is a read that can only fail.
        if (channelId == Guid.Empty)
        {
            return null;
        }

        var channel = _itemInTheLibrary(channelId);

        // A channel with no name is the same answer as no channel. What a
        // report would print for it is nothing either way, and an empty string
        // on the row would read as a channel that is called that.
        return string.IsNullOrEmpty(channel?.Name) ? null : channel.Name;
    }
}
