using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Session;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The transcode reason view explains each reason in one sentence, because the
/// names the server reports mean nothing to somebody who has not read its
/// source. This walks the reasons the server can report and asks the view's own
/// list for each one, so a reason with no sentence is a failing run rather than
/// a blank space on a page nobody has opened yet. Issue #60, first condition.
/// </summary>
/// <remarks>
/// WHAT THIS ASSERTS AND WHAT IT CANNOT SEE, because the difference decides how
/// much a green run is worth. The reasons are a flags enum in the server's own
/// package, so this compares the list against the enum THIS BUILD COMPILES
/// AGAINST, which is the floor named in Directory.Build.props for the framework
/// the suite is running on. A member that appears when that floor is raised is
/// caught here. A server in the field that is newer than the floor and reports
/// a reason nobody has compiled against is NOT caught here, and nothing in this
/// language could catch it: an enum somebody else owns has no exhaustive switch
/// that compiles, and the discard arm that makes one build is the silent gap
/// this file exists against. What answers that case instead is the view, which
/// draws a name it has no sentence for and says so rather than leaving the row
/// blank or dropping it.
/// <para>
/// The suite runs on both framework lines and the two lines carry different
/// enums, so the list has to cover the union of the two and each run sees only
/// its own half of that union. That is why the reverse direction, a sentence
/// for a name no server reports, is not asserted: on the line with the smaller
/// enum, a legitimate entry for the other line is indistinguishable from a
/// misspelling. A misspelling of a name the larger line carries still fails,
/// because the name it was meant to be then has no sentence there.
/// </para>
/// </remarks>
public class TranscodeReasonSentenceTests
{
    /// <summary>
    /// The reasons the server this build compiles against can report. Read off
    /// the enum rather than listed here, for the same reason the capture fold
    /// reads it: a list in a test is a second place for the set to be wrong.
    /// </summary>
    public static TheoryData<string> EveryReason()
    {
        var reasons = new TheoryData<string>();
        foreach (var name in Enum.GetNames<TranscodeReason>())
        {
            reasons.Add(name);
        }

        return reasons;
    }

    /// <summary>
    /// One sentence per reason, keyed on the name the server spells it with,
    /// which is the name the capture fold stores on the row. A reason the view
    /// cannot explain is a row an administrator reads and cannot act on, which
    /// is the whole of what this view is for.
    /// </summary>
    /// <param name="reason">The reason the server can report.</param>
    [Theory]
    [MemberData(nameof(EveryReason))]
    public void EveryReasonTheServerCanReportHasASentence(string reason)
    {
        var sentences = Sentences();

        Assert.True(
            sentences.ContainsKey(reason),
            "Jellyfin.Plugin.Stats/Pages/whyTheServerTranscodes.js has no sentence for "
                + reason
                + ", so a play re-encoded for that reason is drawn under a name and no explanation.");
        Assert.False(
            string.IsNullOrWhiteSpace(sentences[reason]),
            "The sentence for " + reason + " is empty, which reads on the page as a reason nobody explained.");
    }

    /// <summary>
    /// A sentence pasted onto a second reason explains one of the two and
    /// misdescribes the other, and both look explained on the page. This is the
    /// shape a list this long invites, so it is refused rather than left to a
    /// reader of the diff.
    /// </summary>
    [Fact]
    public void NoTwoReasonsAreExplainedWithTheSameSentence()
    {
        var sentences = Sentences();

        var repeated = sentences
            .GroupBy(pair => pair.Value, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(pair => pair.Key)))
            .ToList();

        Assert.True(
            repeated.Count == 0,
            "These reasons share one sentence, so at least one of them is described as something it is not: "
                + string.Join("; ", repeated));
    }

    /// <summary>
    /// The list this suite reads has to be the list the view draws from. A case
    /// that found nothing would pass every assertion above it while the page
    /// explained nothing at all, so the count is asserted before the list is
    /// used for anything.
    /// </summary>
    [Fact]
    public void TheViewCarriesAListOfSentencesForThisSuiteToRead()
    {
        var sentences = Sentences();

        Assert.True(
            sentences.Count >= Enum.GetNames<TranscodeReason>().Length,
            "The view carries "
                + sentences.Count
                + " sentences for "
                + Enum.GetNames<TranscodeReason>().Length
                + " reasons this build knows of, so the list above was read wrongly or has been emptied.");
    }

    /// <summary>
    /// The sentences the view holds, read out of the module the browser loads.
    /// </summary>
    /// <remarks>
    /// The object is found by its name and read to the line that closes it,
    /// rather than every four-space key in the file being taken, so a second
    /// object added to the module later does not quietly join this list.
    /// </remarks>
    /// <returns>The reason names and what each one says.</returns>
    private static IReadOnlyDictionary<string, string> Sentences()
    {
        var module = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.Stats", "Pages", "whyTheServerTranscodes.js"));

        const string Opening = "const REASONS = {";
        var from = module.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(from >= 0, "whyTheServerTranscodes.js no longer declares a REASONS object, so nothing here is being read.");

        var to = module.IndexOf("\n};", from, StringComparison.Ordinal);
        Assert.True(to > from, "The REASONS object in whyTheServerTranscodes.js is not closed on a line of its own.");

        var block = module[from..to];
        var entries = Regex.Matches(
            block,
            @"^\s+(?<name>[A-Za-z][A-Za-z0-9]*):\s*(?<sentence>(?:'[^']*'\s*\+?\s*)+),",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(5));

        var sentences = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match entry in entries)
        {
            sentences[entry.Groups["name"].Value] = Unquote(entry.Groups["sentence"].Value);
        }

        return sentences;
    }

    /// <summary>
    /// The text of a sentence, with the quoting and any wrapping the formatter
    /// introduced taken off, so two sentences that differ only in where the
    /// line broke are one string here.
    /// </summary>
    /// <param name="source">The sentence as it is written in the module.</param>
    /// <returns>What it says.</returns>
    private static string Unquote(string source)
    {
        var pieces = Regex.Matches(source, "'(?<piece>[^']*)'", RegexOptions.None, TimeSpan.FromSeconds(5));
        var text = string.Concat(pieces.Select(piece => piece.Groups["piece"].Value));

        return Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5)).Trim();
    }

    /// <summary>
    /// Finds the top of the working tree from wherever the suite was built to.
    /// </summary>
    /// <returns>The directory holding build.yaml.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "No build.yaml was found above " + AppContext.BaseDirectory + ".");

        return directory!.FullName;
    }
}
