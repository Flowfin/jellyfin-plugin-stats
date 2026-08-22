// The archive, driven over a real store in a temporary directory and over
// strings in memory. A round trip that never touched disk would not test what
// this feature exists to prevent, which is data lost across a move, so both
// ends of every round trip here are a SQLite file.
//
// The archive itself is written to and read from a string rather than a file.
// It carries no path of its own, so there is nothing for a test to clean up and
// nothing for a failed export to leave behind.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class PlayArchiveTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly Guid Bob = Guid.Parse("a1a2a3a4-b1b2-c1c2-d1d2-e1e2e3e4e5e6");

    private readonly string _root;

    public PlayArchiveTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #33. Export, then import into an empty
    /// store, and the row set is the one that was written.
    /// </summary>
    /// <remarks>
    /// Three rows rather than one, and two of them differ only in the fields
    /// that can be absent, because the half of the shape that is null is the
    /// half a format quietly loses. The third shares a start time with the
    /// second, which is what makes the order of the walk a statement rather
    /// than a coincidence.
    /// </remarks>
    [Fact]
    public void AnExportFollowedByAnImportReproducesTheRowSetExactly()
    {
        var written = new[] { APlay(), NothingOptional(), APlay() with { ItemName = "Another episode" } };

        var archive = Export(written);

        var read = ImportIntoAFreshStore(archive, out var added);

        Assert.Equal(3, added);
        Assert.Equal(written.Length, read.Count);
        for (var i = 0; i < written.Length; i++)
        {
            AssertSameRow(written[i], read[i]);
        }
    }

    /// <summary>
    /// The second condition of issue #33. A file from a later schema is refused
    /// and both numbers are in the message, so an administrator can tell which
    /// build to reach for.
    /// </summary>
    [Fact]
    public void AnArchiveFromALaterSchemaIsRefusedWithBothVersionsNamed()
    {
        var later = SchemaMigrations.Latest + 1;
        var archive = Header(later) + Environment.NewLine + RowLine(APlay());

        using var store = new SqlitePlayStore(_root);
        using var archiveReader = new StringReader(archive);

        var refused = Assert.Throws<ArchiveIsNewerThanThePluginException>(
            () => PlayArchive.Import(archiveReader, store));

        Assert.Equal(later, refused.ArchiveVersion);
        Assert.Equal(SchemaMigrations.Latest, refused.PluginVersion);
        Assert.Contains(later.ToString(CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);
        Assert.Contains(SchemaMigrations.Latest.ToString(CultureInfo.InvariantCulture), refused.Message, StringComparison.Ordinal);

        // Refused off the header, so nothing was added before the refusal.
        Assert.Empty(store.AllPlays());
    }

    /// <summary>
    /// The row carries its own version and that one is read too. A header
    /// pasted in front of somebody else's rows is the case this catches, and a
    /// check on the header alone would let those rows in.
    /// </summary>
    [Fact]
    public void ARowFromALaterSchemaIsRefusedEvenWhereTheHeaderIsNot()
    {
        var later = SchemaMigrations.Latest + 1;
        var archive = Header(SchemaMigrations.Latest)
            + Environment.NewLine
            + RowLine(APlay() with { SchemaVersion = later });

        using var store = new SqlitePlayStore(_root);
        using var archiveReader = new StringReader(archive);

        var refused = Assert.Throws<ArchiveIsNewerThanThePluginException>(
            () => PlayArchive.Import(archiveReader, store));

        Assert.Equal(later, refused.ArchiveVersion);
    }

    /// <summary>
    /// The third condition of issue #33. Two users in one store, one of them
    /// exporting, and what comes out is theirs and nothing else.
    /// </summary>
    /// <remarks>
    /// The assertion is over the identifiers on every exported row rather than
    /// over a count, because a count matches a filter that returned the right
    /// number of the wrong rows.
    /// </remarks>
    [Fact]
    public void OneUserExportingCarriesTheirOwnRowsAndNobodyElses()
    {
        var mine = new[]
        {
            APlay() with { UserId = Alice, ItemName = "Mine" },
            APlay() with { UserId = Alice, ItemName = "Also mine" }
        };

        string archive;
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(mine[0]);
            store.Add(APlay() with { UserId = Bob, ItemName = "Not mine" });
            store.Add(mine[1]);
            store.Add(APlay() with { UserId = Bob, ItemName = "Also not mine" });

            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            PlayArchive.Export(store.PlaysFor(Alice), writer);
            archive = writer.ToString();
        }

        var read = ImportIntoAFreshStore(archive, out var added);

        Assert.Equal(2, added);
        Assert.All(read, play => Assert.Equal(Alice, play.UserId));
        Assert.Equal(new[] { "Mine", "Also mine" }, read.Select(play => play.ItemName));
        Assert.DoesNotContain("Not mine", archive, StringComparison.Ordinal);
    }

    /// <summary>
    /// A user with no rows exports an archive that holds none, rather than
    /// falling back to everything.
    /// </summary>
    [Fact]
    public void AUserWithNoRowsExportsAnArchiveWithNoRowsInIt()
    {
        string archive;
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlay() with { UserId = Bob });

            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            PlayArchive.Export(store.PlaysFor(Alice), writer);
            archive = writer.ToString();
        }

        var read = ImportIntoAFreshStore(archive, out var added);

        Assert.Equal(0, added);
        Assert.Empty(read);
    }

    /// <summary>
    /// A field the archive carries and this build has never heard of is refused
    /// rather than dropped, which is what issue #33 asks for by name.
    /// </summary>
    /// <remarks>
    /// Without the refusal, an archive from a build that records something more
    /// imports clean with that something gone and nothing said. The loss is not
    /// visible at the import and not visible afterwards, which is why it is
    /// refused at the only moment the field is still there.
    /// </remarks>
    [Fact]
    public void AFieldThisBuildDoesNotKnowIsRefusedRatherThanDropped()
    {
        var row = "{\"Nickname\":\"someone\"," + RowLine(APlay())[1..];

        AssertTheRowIsRefused(row);
    }

    /// <summary>
    /// The other direction. A row missing a field the shape requires is refused
    /// rather than filled in with a default, because a default here is a play
    /// that says something untrue about what was watched.
    /// </summary>
    [Fact]
    public void ARowMissingAFieldTheShapeRequiresIsRefusedRatherThanDefaulted()
    {
        var row = RowLine(APlay())
            .Replace("\"ItemName\":\"An episode\",", string.Empty, StringComparison.Ordinal);

        AssertTheRowIsRefused(row);
    }

    /// <summary>
    /// A blank line between rows is skipped. A file somebody opened and saved
    /// again is the ordinary way one appears, and losing the import to it would
    /// send an administrator looking for a corruption that is not there.
    /// </summary>
    [Fact]
    public void ABlankLineBetweenRowsCostsNoRow()
    {
        var archive = Header(SchemaMigrations.Latest)
            + Environment.NewLine
            + RowLine(APlay())
            + Environment.NewLine
            + Environment.NewLine
            + RowLine(APlay() with { ItemName = "After the gap" });

        var read = ImportIntoAFreshStore(archive, out var added);

        Assert.Equal(2, added);
        Assert.Equal(new[] { "An episode", "After the gap" }, read.Select(play => play.ItemName));
    }

    [Fact]
    public void AnArchiveWithNoHeaderAtAllIsRefused()
    {
        using var store = new SqlitePlayStore(_root);
        using var nothing = new StringReader(string.Empty);

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(nothing, store));
    }

    /// <summary>
    /// A file that is JSON but is not this format is refused by name, rather
    /// than by whichever field happens to fail to bind first.
    /// </summary>
    [Fact]
    public void AnArchiveOfAnotherFormatIsRefusedAndSaysWhatItReads()
    {
        var archive = "{\"Format\":\"something-else/rows\",\"SchemaVersion\":1}";

        using var store = new SqlitePlayStore(_root);
        using var archiveReader = new StringReader(archive);

        var refused = Assert.Throws<ArgumentException>(
            () => PlayArchive.Import(archiveReader, store));

        Assert.Contains("something-else/rows", refused.Message, StringComparison.Ordinal);
        Assert.Contains(PlayArchive.FormatName, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A line that parses as JSON and holds no object at all. Every line of an
    /// archive is a header or a play, and a null is neither.
    /// </summary>
    [Fact]
    public void ALineHoldingNoObjectIsRefused()
    {
        using var store = new SqlitePlayStore(_root);
        using var noObject = new StringReader("null");

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(noObject, store));
    }

    [Fact]
    public void NeitherEndOfTheArchiveTakesNothing()
    {
        using var store = new SqlitePlayStore(_root);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        using var reader = new StringReader("null");

        Assert.Throws<ArgumentNullException>(() => PlayArchive.Export(null!, writer));
        Assert.Throws<ArgumentNullException>(() => PlayArchive.Export([], null!));
        Assert.Throws<ArgumentNullException>(() => PlayArchive.Import(null!, store));
        Assert.Throws<ArgumentNullException>(() => PlayArchive.Import(reader, null!));
    }

    /// <summary>
    /// The walk over the whole store is in the order the rows were written, and
    /// the two rows sharing a start time are what makes that a statement. A
    /// walk ordered by the start time alone would put them in whichever order
    /// the query planner produced, and the round trip above would then differ
    /// from its original for a reason that has nothing to do with the archive.
    /// </summary>
    [Fact]
    public void TheWalkOverTheStoreIsInTheOrderTheRowsWereWritten()
    {
        var moment = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

        using var store = new SqlitePlayStore(_root);
        store.Add(APlay() with { ItemName = "first", StartedUtc = moment });
        store.Add(APlay() with { ItemName = "second", StartedUtc = moment.AddHours(-3) });
        store.Add(APlay() with { ItemName = "third", StartedUtc = moment });

        Assert.Equal(
            new[] { "first", "second", "third" },
            store.AllPlays().Select(play => play.ItemName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Puts a header in front of one row and asserts the import refuses it and
    /// adds nothing.
    /// </summary>
    /// <param name="row">The row line.</param>
    private void AssertTheRowIsRefused(string row)
    {
        var archive = Header(SchemaMigrations.Latest) + Environment.NewLine + row;

        using var store = new SqlitePlayStore(_root);
        using var archiveReader = new StringReader(archive);

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(archiveReader, store));
        Assert.Empty(store.AllPlays());
    }

    /// <summary>
    /// A row written before the delivery method was named for the moment it is
    /// about is moved forward and read. Issue #158 is the first schema that
    /// changed the row, and until it there was nothing for the step list to do.
    /// </summary>
    [Fact]
    public void ARowFromTheSchemaBeforeTheRenameIsMovedForwardAndRead()
    {
        var archive = Header(3) + Environment.NewLine + AsWrittenUnderSchemaThree(APlay());

        var read = Assert.Single(ImportIntoAFreshStore(archive, out var added));

        Assert.Equal(1, added);
        Assert.Equal(APlay().PlayMethodAtStart, read.PlayMethodAtStart);

        // Nothing was watching for the change when that row was recorded, so
        // empty is the honest answer rather than a moment worked out later.
        Assert.Null(read.PlayMethodChangedUtc);
    }

    /// <summary>
    /// The old spelling is refused where the row says it is from the newest
    /// schema, so a row that was edited by hand into the wrong version is not
    /// quietly moved forward twice.
    /// </summary>
    [Fact]
    public void TheOldSpellingIsRefusedFromARowThatClaimsTheNewestSchema()
    {
        var archive = Header(SchemaMigrations.Latest)
            + Environment.NewLine
            + AsWrittenUnderSchemaThree(APlay()).Replace(
                "\"SchemaVersion\":3",
                "\"SchemaVersion\":" + SchemaMigrations.Latest.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal);

        using var store = new SqlitePlayStore(Path.Combine(_root, "read"));
        using var archiveReader = new StringReader(archive);

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(archiveReader, store));
    }

    /// <summary>
    /// An older row that is missing the method altogether is refused rather
    /// than moved forward into a row with a delivery method nobody reported.
    /// The step renames what is there; it does not invent what is not.
    /// </summary>
    [Fact]
    public void AnOlderRowMissingTheMethodIsRefusedRatherThanDefaulted()
    {
        var archive = Header(3)
            + Environment.NewLine
            + Regex.Replace(
                AsWrittenUnderSchemaThree(APlay()),
                "\"PlayMethod\":[0-9]+,",
                string.Empty,
                RegexOptions.None,
                TimeSpan.FromSeconds(5));

        using var store = new SqlitePlayStore(Path.Combine(_root, "read"));
        using var archiveReader = new StringReader(archive);

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(archiveReader, store));
    }

    /// <summary>
    /// A row carrying no schema version is refused. Which shape a row is in is
    /// what decides whether it needs a step, and a row that does not say is a
    /// row nothing can place.
    /// </summary>
    [Fact]
    public void ARowThatSaysNoVersionIsRefused()
    {
        var archive = Header(SchemaMigrations.Latest)
            + Environment.NewLine
            + "{\"UserId\":\"6f9619ff8b86d011b42d00c04fc964ff\"}";

        using var store = new SqlitePlayStore(Path.Combine(_root, "read"));
        using var archiveReader = new StringReader(archive);

        Assert.Throws<ArgumentException>(() => PlayArchive.Import(archiveReader, store));
    }

    /// <summary>
    /// One play as the build before the rename wrote it: the old column name,
    /// no moment beside it, and the version that build stamped.
    /// </summary>
    /// <remarks>
    /// Derived from what this build writes rather than pasted, so the fields
    /// this issue did not touch stay in step with the shape and the fixture
    /// cannot rot into one no build ever produced.
    /// </remarks>
    /// <param name="play">The play to write.</param>
    /// <returns>The line.</returns>
    private static string AsWrittenUnderSchemaThree(PlayRecord play)
    {
        return RowLine(play)
            .Replace(
                "\"SchemaVersion\":" + SchemaMigrations.Latest.ToString(CultureInfo.InvariantCulture),
                "\"SchemaVersion\":3",
                StringComparison.Ordinal)
            .Replace("\"PlayMethodAtStart\":", "\"PlayMethod\":", StringComparison.Ordinal)
            .Replace(",\"PlayMethodChangedUtc\":null", string.Empty, StringComparison.Ordinal);
    }

    private static string Header(int schemaVersion)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Format\":\"{0}\",\"SchemaVersion\":{1}}}",
            PlayArchive.FormatName,
            schemaVersion);
    }

    /// <summary>
    /// One play as the export writes it, without the header above it.
    /// </summary>
    /// <param name="play">The play to write.</param>
    /// <returns>The line.</returns>
    private static string RowLine(PlayRecord play)
    {
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PlayArchive.Export([play], writer);

        return writer.ToString()
            .Split('\n')[1]
            .TrimEnd('\r');
    }

    /// <summary>
    /// Compares a row that was exported with the row that came back out of the
    /// store it was imported into.
    /// </summary>
    /// <remarks>
    /// Record equality compares the reason list by reference, so a row that
    /// survived perfectly still fails a plain comparison. The list is compared
    /// as a sequence first and the reference is then made equal, so what the
    /// record comparison after it covers is every other field at once,
    /// including any field added to the shape later.
    /// </remarks>
    /// <param name="expected">The row that was exported.</param>
    /// <param name="actual">The row that came back.</param>
    private static void AssertSameRow(PlayRecord expected, PlayRecord actual)
    {
        Assert.Equal(expected.Transcode.Reasons, actual.Transcode.Reasons);

        var comparable = expected with
        {
            Transcode = expected.Transcode with { Reasons = actual.Transcode.Reasons }
        };

        Assert.Equal(comparable, actual);
    }

    private string Export(IReadOnlyList<PlayRecord> plays)
    {
        using var store = new SqlitePlayStore(Path.Combine(_root, "written"));
        foreach (var play in plays)
        {
            store.Add(play);
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        PlayArchive.Export(store.AllPlays(), writer);

        return writer.ToString();
    }

    private IReadOnlyList<PlayRecord> ImportIntoAFreshStore(string archive, out int added)
    {
        using var store = new SqlitePlayStore(Path.Combine(_root, "read"));
        using var archiveReader = new StringReader(archive);

        added = PlayArchive.Import(archiveReader, store);

        return store.AllPlays().ToList();
    }

    private static PlayRecord NothingOptional()
    {
        return APlay() with
        {
            ParentId = null,
            ItemRuntime = null,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    private static PlayRecord APlay()
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Alice,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.Transcode,
            PlayMethodChangedUtc = null,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = true,
                PeakBitrate = 8_000_000,
                TypicalBitrate = 6_000_000,
                HardwareAcceleration = "qsv",
                Reasons = ["VideoCodecNotSupported", "AudioBitrateNotSupported"]
            }
        };
    }
}
