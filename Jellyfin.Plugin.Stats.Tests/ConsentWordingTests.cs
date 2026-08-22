// The words a person agrees to, and the number their agreement points at.
//
// A stored agreement is worth what can be said about it afterwards. If the
// wording can move under a version that does not, then a record saying somebody
// agreed to version 1 says nothing: version 1 is whatever the file happens to
// hold today. The failure is silent and it is the worst kind here, because what
// it produces is a person recorded as having agreed to text they were never
// shown.
//
// So the version is not something to remember to raise. The text of each
// version is fingerprinted below, and changing a word without moving the number
// fails the run. Moving the number without recording what it holds fails it too,
// which is the half that stops the fingerprint being quietly retired.
//
// The fingerprint is taken over the text with line endings normalised, and that
// is a deliberate weakening: this repository has no attributes file, so a clone
// on Windows holds the same words with different bytes, and a fingerprint over
// the raw bytes would pass on one platform and fail on the other for a reason
// nobody wrote. A change to line endings alone therefore walks through, which
// is a change no wording is about.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.Stats.Privacy;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class ConsentWordingTests
{
    /// <summary>
    /// One entry per version of the wording that has ever been declared, and
    /// the fingerprint of the words it declared. Adding a version is adding a
    /// line here, which is the deliberate act a version bump is supposed to be.
    /// A line is never edited: an edited line is the silent change this file
    /// exists against, wearing the clothes of a repair.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> Fingerprints = new Dictionary<int, string>
    {
        [1] = "31cd4139f1ae89b0d0a1cc7394396d5c1db167524c4cff2a51f9730ccb511ce4",
    };

    /// <summary>
    /// The words cannot move under a version that does not.
    /// </summary>
    [Fact]
    public void TheWordingCannotChangeWithoutItsVersionChanging()
    {
        Assert.True(
            Fingerprints.TryGetValue(ConsentWording.Version, out var recorded),
            "The wording declares version "
                + ConsentWording.Version.ToString(CultureInfo.InvariantCulture)
                + " and no fingerprint is recorded for it. A new version is a new line in this file, added in the same change as the words.");

        Assert.True(
            string.Equals(recorded, Fingerprint(ConsentWording.Text), StringComparison.Ordinal),
            "The words of version "
                + ConsentWording.Version.ToString(CultureInfo.InvariantCulture)
                + " are not the words recorded for it. Either raise the version in Jellyfin.Plugin.Stats/Privacy/consent.txt and record the new one here, or put the text back; a person who agreed to this version agreed to what was recorded.");
    }

    /// <summary>
    /// Every version ever declared keeps its fingerprint, so a record pointing
    /// at an old one still names something.
    /// </summary>
    [Fact]
    public void NoVersionBelowTheCurrentOneIsMissing()
    {
        var missing = Enumerable.Range(1, ConsentWording.Version)
            .Where(version => !Fingerprints.ContainsKey(version))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "These versions have been declared at some point and nothing here records what they said: "
                + string.Join(", ", missing.Select(version => version.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>
    /// The version the plugin reports is the one written in the words a person
    /// is shown, rather than a second copy of the number kept in C#.
    /// </summary>
    [Fact]
    public void TheVersionIsReadOutOfTheWordingItself()
    {
        var declared = File.ReadAllLines(WordingFile())[0];

        Assert.Equal(
            "Version: " + ConsentWording.Version.ToString(CultureInfo.InvariantCulture),
            declared);
    }

    /// <summary>
    /// The wording travels in the assembly, byte for byte as it is tracked, so
    /// what a stored agreement points at is what was built and reviewed rather
    /// than a file somebody dropped beside the plugin.
    /// </summary>
    [Fact]
    public void TheWordingIsEmbeddedByteForByteAsItIsTracked()
    {
        using var stream = typeof(Plugin).Assembly.GetManifestResourceStream(ConsentWording.ResourceName);

        Assert.True(
            stream is not null,
            "The assembly embeds no resource named "
                + ConsentWording.ResourceName
                + ", so nothing a person is shown travels with the plugin.");

        using var buffer = new MemoryStream();
        stream!.CopyTo(buffer);

        Assert.Equal(File.ReadAllBytes(WordingFile()), buffer.ToArray());
    }

    /// <summary>
    /// The words themselves say what a person is agreeing to. This asserts the
    /// three statements the rest of this plan is built on are in them, because a
    /// wording that dropped one would still fingerprint cleanly under a new
    /// version and nothing else would notice.
    /// </summary>
    [Theory]
    [InlineData("without your agreement")]
    [InlineData("withdraw at any time")]
    [InlineData("not a condition of using this server")]
    public void TheWordingSaysWhatAgreeingChanges(string sentence)
    {
        Assert.Contains(sentence, ConsentWording.Text, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fingerprint(string text)
    {
        var normalised = text.Replace("\r\n", "\n", StringComparison.Ordinal);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    private static string WordingFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "No build.yaml was found above " + AppContext.BaseDirectory + ".");

        return Path.Combine(directory!.FullName, "Jellyfin.Plugin.Stats", "Privacy", "consent.txt");
    }
}
