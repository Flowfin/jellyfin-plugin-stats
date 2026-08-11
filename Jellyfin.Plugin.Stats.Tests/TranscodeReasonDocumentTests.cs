using System;
using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// docs/transcode-reasons.md exists to stop a reader concluding the plugin
/// counts wrong when the reason rows add up to more than the plays. That
/// sentence is only true while a play can carry several reasons and exactly one
/// delivery method, so the document is an assertion about the row shape and not
/// only about prose.
/// <para>
/// A document that quietly stops matching the model is worse than none, because
/// it is read as a statement about the code rather than as something somebody
/// wrote once. So the delivery methods are checked against the row here, and the
/// sentence a reader needs is checked to still be in the file. What this cannot
/// judge is whether the rest of the prose is right, which is what the review is
/// for. Issue #53, second condition.
/// </para>
/// <para>
/// The other half of the document's basis, that the reasons are a collection on
/// the play rather than one value, carries no assertion here on purpose. Making
/// that change stops the tree compiling in the fold and in the store before any
/// assertion runs, so a test for it could never be shown to fire for the reason
/// it names, and the compiler is the thing holding it.
/// </para>
/// </summary>
public class TranscodeReasonDocumentTests
{
    /// <summary>
    /// The delivery method is one value per play, which is why the shares over
    /// it do add up to the play count while the reason rows do not. The document
    /// names each value and says how many there are. A value added to the row
    /// without the document moving is the drift this catches, and the count is
    /// asserted separately from the names so the failure says which half moved.
    /// </summary>
    [Fact]
    public void TheDocumentNamesEveryDeliveryMethodTheRowCanHold()
    {
        var document = Prose();

        foreach (var method in Enum.GetNames<PlayMethod>())
        {
            Assert.Contains(method, document, StringComparison.Ordinal);
        }

        Assert.Equal(4, Enum.GetValues<PlayMethod>().Length);
        Assert.Contains("Four values, one per play", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence the issue's second condition asks for, in the two places the
    /// document makes the point: the heading a reader arrives at, and the
    /// paragraph that says what adding the rows up produces. A rewrite that
    /// loses both has lost the reason the file exists while leaving the file.
    /// </summary>
    [Fact]
    public void TheDocumentSaysTheReasonCountsExceedThePlayCount()
    {
        var document = Prose();

        Assert.Contains("more than the plays", document, StringComparison.Ordinal);
        Assert.Contains("larger than the number of plays", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The document this file is about, with every run of whitespace collapsed
    /// to one space. The sentences it is read for are wrapped at eighty columns,
    /// so a phrase this asks for sits across a line break as often as not, and a
    /// test that matched the raw bytes would pass or fail on where the wrapping
    /// fell rather than on what the document says.
    /// </summary>
    /// <returns>Its text, on one line.</returns>
    private static string Prose()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "transcode-reasons.md"));

        return Regex.Replace(text, @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(5));
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
