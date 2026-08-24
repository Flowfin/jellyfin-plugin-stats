using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// {"Format":"jellyfin-plugin-stats/plays","SchemaVersion":4}
/// {"SchemaVersion":4,"UserId":"...","ItemId":"...", ... }
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
/// second condition of issue #33. An archive from an earlier one is moved
/// forward a step at a time before it is read, and the version each row needs
/// is on the row rather than only on the header, so a file assembled by hand
/// out of two exports is moved row by row. Issue #158 is the schema that first
/// changed the row and this is the step list it owed.
/// </para>
/// <para>
/// The steps work on the object as it was written rather than on the record,
/// because the record is the newest shape and an older row cannot be read into
/// it: that is the whole reason a step exists. Each one is named for what it
/// does to a row and the list is walked in version order, so a second one is a
/// line here rather than a rewrite.
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

            var written = ReadObject<JsonObject>(line);

            // The row carries its own version as well as the header, and this
            // reads the row's. A file assembled by hand, or one header line
            // pasted in front of somebody else's rows, is the case where the
            // two disagree, and the row is the one that says what the fields
            // actually mean.
            var version = VersionOf(written);
            RefuseALaterSchema(version);

            var play = ReadObject<PlayRecord>(MovedForward(written, version).ToJsonString(Format));

            into.Add(play);
            added++;
        }

        return added;
    }

    /// <summary>
    /// Reads the schema version off a row as it was written.
    /// </summary>
    /// <remarks>
    /// Off the object rather than off the record, because the record is the
    /// newest shape and a row that needs a step cannot be read into it yet.
    /// </remarks>
    /// <param name="written">The row.</param>
    /// <returns>The version the row was written under.</returns>
    private static int VersionOf(JsonObject written)
    {
        if (written.TryGetPropertyValue(nameof(PlayRecord.SchemaVersion), out var version)
            && version is not null
            && version.GetValueKind() == JsonValueKind.Number)
        {
            return version.GetValue<int>();
        }

        throw new ArgumentException(
            "A line of the archive carries no schema version. Every row written by this plugin does, and it is what says which shape the row is in.");
    }

    /// <summary>
    /// Moves one row forward through every step it has not had.
    /// </summary>
    /// <param name="written">The row as it was written.</param>
    /// <param name="version">The version it was written under.</param>
    /// <returns>The row in the shape this build reads.</returns>
    private static JsonObject MovedForward(JsonObject written, int version)
    {
        if (version < 4)
        {
            // Issue #158 named the delivery method for the moment it is about
            // and added the moment it changed. A row written before that has
            // the old name and no such moment, and null is the honest answer:
            // nothing was watching for the change when it was recorded.
            if (written.Remove("PlayMethod", out var method))
            {
                written[nameof(PlayRecord.PlayMethodAtStart)] = method;
            }

            written[nameof(PlayRecord.PlayMethodChangedUtc)] = null;
        }

        if (version < 6)
        {
            // Issue #222 added which route ended the play. A row written before
            // it does not say, and that is the value rather than a guess: the
            // route was not recorded when the row was written, so nothing about
            // this row can tell a clean ending from one something gave up
            // waiting for.
            //
            // IT FILLS AN ABSENCE AND NEVER OVERWRITES AN ANSWER, which the
            // step above does not do and is a defect of its own rather than a
            // style this one is copying. A row the capture writes today says it
            // is at version one, because the number it stamps is the one the row
            // shape was decided at rather than the store's, so a row carrying a
            // real answer arrives here reading as older than the column it
            // carries. Under a bare assignment that answer would be replaced by
            // this one on the way in. Issue #222 carries the reading.
            if (!written.ContainsKey(nameof(PlayRecord.ClosedBy)))
            {
                written[nameof(PlayRecord.ClosedBy)] = (int)PlayClosedBy.NotSaid;
            }
        }

        return written;
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
