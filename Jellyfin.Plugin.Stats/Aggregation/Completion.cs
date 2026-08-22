using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// How much of an item a play got through, where that means anything, and
/// nothing at all where it does not.
/// </summary>
/// <remarks>
/// Not everything played has a length. Live television has no end, a radio
/// stream has no runtime, and a photograph reports a position that means
/// nothing. A share computed over those is arithmetic on a value that does not
/// exist, and it arrives at a reader as a real figure: a channel somebody left
/// on all evening reads as a programme abandoned after two per cent. So the
/// answer here is absent rather than nought, and a report over it says how many
/// rows it left out rather than folding them in as zeroes. Issue #40.
/// <para>
/// Two things decide it, and both have to hold. The item's kind has to be one
/// with a length a play can get through, which is the list below, and the row
/// has to carry a runtime, which the server does not always report even for a
/// kind that usually has one. The kind is asked first, so a live television row
/// that arrived with a scheduled programme length is left out as live
/// television rather than counted as a film.
/// </para>
/// <para>
/// WHAT THE LIST CAN AND CANNOT SEE. It is keyed on the name the server spells
/// an item kind with, which is what the capture fold stores on the row, and it
/// is compared against the kinds THIS BUILD COMPILES AGAINST by
/// <c>CompletionTests</c>. A kind that appears when the floor in
/// <c>Directory.Build.props</c> is raised is caught there. A server in the
/// field newer than that floor, reporting a kind nobody here has compiled
/// against, is not caught by anything, and no reading of an enum somebody else
/// owns could catch it. That case is answered by what this does with it instead
/// of by a failing build: a kind this build has never seen carries no
/// completion, and the report counts it among the rows it left out, so a
/// figure is never invented for something nobody here has classified.
/// </para>
/// <para>
/// The two lines carry different enums, so the list covers the union of them
/// and each run sees only its own half. That is why nothing asserts the other
/// direction, an entry here for a name no server reports: on the line with the
/// smaller enum a legitimate entry for the other line cannot be told from a
/// misspelling. A misspelling of a name the larger line carries still fails
/// there, because the kind it was meant to be is then accounted for by nothing.
/// It is the same bound as the transcode reason sentences, and it is written
/// out here rather than pointed at because these are two different lists.
/// </para>
/// </remarks>
public static class Completion
{
    /// <summary>
    /// The kinds whose plays have a length to get through. Everything that is
    /// watched or listened to end to end, and nothing else: a recording is here
    /// because it is a finished file with a length, and the live television
    /// kinds are not, because a channel does not end and a programme's
    /// scheduled length says nothing about when somebody joined it.
    /// </summary>
    private static readonly HashSet<string> WithALengthToGetThrough = new(StringComparer.Ordinal)
    {
        "Audio",
        "AudioBook",
        "Episode",
        "Movie",
        "MusicVideo",
        "Recording",
        "Trailer",
        "Video",
    };

    /// <summary>
    /// The kinds whose plays have no length to get through. A folder, a
    /// container, a person and a year are not played at all and are here
    /// because being accounted for is what this file promises; the interesting
    /// entries are the ones that are played and still have no share to compute,
    /// which are live television, a photograph and a book.
    /// </summary>
    private static readonly HashSet<string> WithNothingToGetThrough = new(StringComparer.Ordinal)
    {
        "AggregateFolder",
        "BasePluginFolder",
        "Book",
        "BoxSet",
        "Channel",
        "ChannelFolderItem",
        "CollectionFolder",
        "Folder",
        "Genre",
        "LiveTvChannel",
        "LiveTvProgram",
        "ManualPlaylistsFolder",
        "MusicAlbum",
        "MusicArtist",
        "MusicGenre",
        "Person",
        "Photo",
        "PhotoAlbum",
        "Playlist",
        "PlaylistsFolder",
        "Program",
        "Season",
        "Series",
        "Studio",
        "TvChannel",
        "TvProgram",
        "UserRootFolder",
        "UserView",
        "Year",
    };

    /// <summary>
    /// Says whether this build has an answer about an item kind at all.
    /// </summary>
    /// <remarks>
    /// The suite asks this of every kind the server can report, so a kind
    /// nobody classified is a failing run rather than a row that quietly stops
    /// being counted. It is not what the reading below asks: a kind that is
    /// unaccounted for is treated as one with nothing to get through, which is
    /// the answer that cannot produce a wrong figure.
    /// </remarks>
    /// <param name="itemType">The kind, spelled the way the row stores it.</param>
    /// <returns>True where this build classifies that kind.</returns>
    public static bool IsAccountedFor(string itemType)
        => WithALengthToGetThrough.Contains(itemType) || WithNothingToGetThrough.Contains(itemType);

    /// <summary>
    /// Says whether a play of this kind of item can have a completion at all.
    /// </summary>
    /// <param name="itemType">The kind, spelled the way the row stores it.</param>
    /// <returns>True where a play of that kind has a length to get through.</returns>
    public static bool CanBeComputedFor(string itemType) => WithALengthToGetThrough.Contains(itemType);

    /// <summary>
    /// Reads how much of the item a play got through.
    /// </summary>
    /// <remarks>
    /// Read off the row rather than stored on it, because the row already holds
    /// everything the share is computed from and a stored figure would be a
    /// second answer that could disagree with the two it came from.
    /// <para>
    /// It is the watched time over the item's length, and the watched time is
    /// the one the capture fold already keeps, so a session left paused for an
    /// hour does not read as an hour watched. A play that ran longer than the
    /// item is whole rather than more than whole: somebody who rewinds watches
    /// more minutes than the film is long, and a share above one in an average
    /// would make the average say less than nothing. What that costs is that
    /// this cannot be read backwards into how long somebody sat there, and the
    /// watched duration on the row is where that question is answered.
    /// </para>
    /// <para>
    /// The other end is clamped as well, and it is there for a row this build
    /// did not write. The capture fold adds nothing negative to a watched time,
    /// so no row written here can be below nought, but a stored row outlives
    /// the assembly that wrote it and is read by this one. Nought is the
    /// closest true thing to say about such a row, and it is said rather than a
    /// negative share being carried into an average.
    /// </para>
    /// </remarks>
    /// <param name="play">The stored play.</param>
    /// <returns>The share between nought and one, or null where the play has none.</returns>
    public static double? Of(PlayRecord play)
    {
        ArgumentNullException.ThrowIfNull(play);

        if (!CanBeComputedFor(play.ItemType))
        {
            return null;
        }

        if (play.ItemRuntime is not TimeSpan runtime || runtime <= TimeSpan.Zero)
        {
            return null;
        }

        return Math.Clamp(play.WatchedDuration / runtime, 0, 1);
    }
}
