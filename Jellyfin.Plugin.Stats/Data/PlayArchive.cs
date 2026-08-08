using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// The plugin's own data, written out in a plain format and read back in.
/// </summary>
/// <remarks>
/// <para>
/// The format is one JSON object per line. The first line is a header naming
/// the format and the schema version the rows were written under; every line
/// after it is one play, with the property names of <see cref="PlayRecord"/>
/// and its nested transcode summary:
/// </para>
/// <code>
/// {"Format":"jellyfin-plugin-stats/plays","SchemaVersion":2}
/// {"SchemaVersion":2,"UserId":"...","ItemId":"...", ... }
/// </code>
/// <para>
/// A line at a time rather than one array holding everything, because both ends
/// of this are a walk: the export writes a row as it reads it and the import
/// adds a row as it reads it, so a store with a year in it is never in memory
/// at once on either side. The lines are also readable with any tool that reads
/// text, which is what a backup an administrator has to be able to check is
/// worth.
/// </para>
/// <para>
/// Nothing here opens a file or names a path. The export is handed somewhere to
/// write and the import somewhere to read, so an export that fails leaves
/// nothing behind for the caller to clean up, and the suite drives both over
/// memory rather than over a temporary directory it has to sweep. Where those
/// ends are is the caller's, and for the plugin's own folder that is issue #73.
/// </para>
/// <para>
/// An archive from a later schema is refused rather than read, which is the
/// second condition of issue #33. An archive from an earlier one is read as it
/// stands: every schema this plugin has shipped writes the same row, so there
/// is nothing yet to convert. The first schema that changes the row is the one
/// that owes this a step list, and it will find the version it needs on the
/// header line and on every row.
/// </para>
/// </remarks>
public static class PlayArchive
{
    /// <summary>
    /// What the header line calls this format. It is checked on import, so a
    /// file that is JSON but is not this is refused by name rather than by the
    /// first field that fails to bind.
    /// </summary>
    public const string FormatName = "jellyfin-plugin-stats/plays";

    /// <remarks>
    /// Disallowing an unmapped member is the whole of "never silently drops a
    /// field it does not recognise". Without it the reader's default is to skip
    /// what it cannot place, so an archive carrying a column this build has
    /// never heard of would import cleanly with that column gone and nothing
    /// said. Required members hold the other direction: a row missing a field
    /// is refused rather than defaulted.
    /// </remarks>
    private static readonly JsonSerializerOptions Format = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Writes a header and then every play handed to it, one per line.
    /// </summary>
    /// <param name="plays">The rows to write, walked once.</param>
    /// <param name="destination">Where to write them.</param>
    public static void Export(IEnumerable<PlayRecord> plays, TextWriter destination)
    {
        ArgumentNullException.ThrowIfNull(plays);
        ArgumentNullException.ThrowIfNull(destination);

        var header = new ArchiveHeader
        {
            Format = FormatName,
            SchemaVersion = SchemaMigrations.Latest
        };

        destination.WriteLine(JsonSerializer.Serialize(header, Format));

        foreach (var play in plays)
        {
            destination.WriteLine(JsonSerializer.Serialize(play, Format));
        }
    }

    /// <summary>
    /// Reads an archive back and adds every row in it to a store.
    /// </summary>
    /// <remarks>
    /// The rows go in as they are read, so an archive that is refused part of
    /// the way through leaves the rows before the bad line in the store. The
    /// refusals that can happen before any row is added are the ones that carry
    /// a whole file's worth of meaning - the wrong format and a later schema -
    /// and both are made off the header line, before the first play is touched.
    /// </remarks>
    /// <param name="source">The archive to read.</param>
    /// <param name="into">The store to add the rows to.</param>
    /// <returns>How many rows were added.</returns>
    public static int Import(TextReader source, IPlayStore into)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(into);

        var headerLine = source.ReadLine();
        if (headerLine is null)
        {
            throw new ArgumentException(
                "The archive is empty. Its first line is a header naming the format and the schema version, and a file without one is not an archive this plugin wrote.",
                nameof(source));
        }

        var header = ReadObject<ArchiveHeader>(headerLine);
        if (!string.Equals(header.Format, FormatName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The archive says its format is {0} and this reads {1}. Nothing was imported.",
                    header.Format,
                    FormatName),
                nameof(source));
        }

        RefuseALaterSchema(header.SchemaVersion);

        var added = 0;
        while (source.ReadLine() is { } line)
        {
            // A blank line carries no row. It is skipped rather than refused
            // because a text file that somebody opened and saved again is the
            // ordinary way one appears, and losing an import to it would send
            // an administrator looking for a corruption that is not there.
            if (line.Length == 0)
            {
                continue;
            }

            var play = ReadObject<PlayRecord>(line);

            // The row carries its own version as well as the header, and this
            // reads the row's. A file assembled by hand, or one header line
            // pasted in front of somebody else's rows, is the case where the
            // two disagree, and the row is the one that says what the fields
            // actually mean.
            RefuseALaterSchema(play.SchemaVersion);

            into.Add(play);
            added++;
        }

        return added;
    }

    private static void RefuseALaterSchema(int version)
    {
        if (version > SchemaMigrations.Latest)
        {
            throw new ArchiveIsNewerThanThePluginException(version, SchemaMigrations.Latest);
        }
    }

    private static T ReadObject<T>(string line)
    {
        T? read;
        try
        {
            read = JsonSerializer.Deserialize<T>(line, Format);
        }
        catch (JsonException problem)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "A line of the archive could not be read: {0}",
                    problem.Message),
                problem);
        }

        if (read is null)
        {
            throw new ArgumentException(
                "A line of the archive holds no object. Every line is a header or a play.");
        }

        return read;
    }

    /// <summary>
    /// The first line of an archive.
    /// </summary>
    /// <remarks>
    /// A type of its own rather than two loose values, so the header goes
    /// through the same reader as the rows and gets the same refusal of a field
    /// nobody recognises.
    /// </remarks>
    internal sealed record ArchiveHeader
    {
        /// <summary>
        /// Gets what this file is.
        /// </summary>
        public required string Format { get; init; }

        /// <summary>
        /// Gets the schema version the rows were written under.
        /// </summary>
        public required int SchemaVersion { get; init; }
    }
}
