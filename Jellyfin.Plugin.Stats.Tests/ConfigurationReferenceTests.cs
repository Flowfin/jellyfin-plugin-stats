using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Stats.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// docs/configuration.md is the only written account of what this plugin can be
/// told. A reference typed out beside the model is right on the day it is
/// written: a field added later has no entry, a field removed later keeps one,
/// and a default moved in ConfigurationLimits leaves a number in the document
/// that an operator plans a retention around.
/// <para>
/// So the table is compared against the model rather than read alongside it. The
/// defaults come from a fresh <see cref="PluginConfiguration"/>, which is the
/// same object a server with no stored file gets, and the ranges come from
/// <see cref="ConfigurationLimits"/>, which is where the setters read them.
/// </para>
/// <para>
/// What this cannot check is whether the prose is true. Nothing here judges
/// whether an entry describes what the setting does, and the retention check
/// below holds that four statements are present rather than that they are
/// correct. Issue #78.
/// </para>
/// </summary>
public class ConfigurationReferenceTests
{
    /// <summary>
    /// What a cell says where a field's default is an empty list, or where the
    /// field carries no value of its own.
    /// </summary>
    private const string Empty = "empty";

    /// <summary>
    /// The heading of the section the retention statements have to be in.
    /// </summary>
    private const string RetentionHeading = "Retention deletes, and the deletion cannot be undone";

    /// <summary>
    /// A row of the table, under the field it is about.
    /// </summary>
    /// <param name="Setting">The name of the field on the configuration model.</param>
    /// <param name="Default">The default the entry claims.</param>
    /// <param name="Accepted">What the entry says the field accepts.</param>
    /// <param name="Does">What the entry says the field does.</param>
    private sealed record Row(string Setting, string Default, string Accepted, string Does);

    /// <summary>
    /// The table has an entry for every field on the model and for no other. A
    /// setting added without an entry is the drift this file exists against, and
    /// an entry left behind by a removed setting is the same defect pointing the
    /// other way: an operator reading about a control the plugin does not have.
    /// </summary>
    [Fact]
    public void TheTableCoversExactlyTheFieldsTheModelHas()
    {
        var model = Fields().Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal);
        var documented = Table().Select(row => row.Setting).OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(model, documented);
    }

    /// <summary>
    /// Every entry names the default the model actually starts from. The value is
    /// read off an object built the way the server builds one for a server with no
    /// stored configuration file, so this compares the document against the
    /// behaviour rather than against a second copy of the constants.
    /// </summary>
    [Fact]
    public void EveryEntryNamesTheDefaultTheModelStartsFrom()
    {
        var fresh = new PluginConfiguration();
        var fields = Fields().ToDictionary(field => field.Name, StringComparer.Ordinal);

        foreach (var row in Table())
        {
            Assert.True(
                fields.TryGetValue(row.Setting, out var field),
                "The table has an entry for " + row.Setting + ", which is not a field on the configuration model.");

            Assert.Equal(Rendered(field!.GetValue(fresh)), row.Default);
        }
    }

    /// <summary>
    /// Every entry for a bounded number names the bounds the setter enforces. A
    /// document claiming a wider range than the model accepts sends an operator
    /// to a value that is silently refused and replaced by the default, which is
    /// the one failure mode this plugin's refusal behaviour cannot warn about in
    /// advance.
    /// </summary>
    /// <remarks>
    /// The check reads the phrase "from `min` to `max`", so the entries for the
    /// bounded fields have to carry the range in that form. That is a convention
    /// this check imposes on the document, and the alternative was parsing the
    /// prose, which fails in the direction that lets a wrong range through.
    /// </remarks>
    [Fact]
    public void EveryBoundedEntryNamesTheRangeTheSetterEnforces()
    {
        var bounded = new Dictionary<string, (int Minimum, int Maximum)>(StringComparer.Ordinal)
        {
            [nameof(PluginConfiguration.PlayRowRetentionDays)] = (ConfigurationLimits.MinimumDays, ConfigurationLimits.MaximumDays),
            [nameof(PluginConfiguration.DailyAggregateRetentionDays)] = (ConfigurationLimits.MinimumDays, ConfigurationLimits.MaximumDays),
            [nameof(PluginConfiguration.MaximumRangeDays)] = (ConfigurationLimits.MinimumDays, ConfigurationLimits.MaximumDays),
            [nameof(PluginConfiguration.MaximumRowsPerResponse)] = (ConfigurationLimits.MinimumRowsPerResponse, ConfigurationLimits.MaximumRowsPerResponse)
        };

        foreach (var row in Table())
        {
            if (!bounded.TryGetValue(row.Setting, out var range))
            {
                continue;
            }

            var phrase = string.Format(
                CultureInfo.InvariantCulture,
                "from `{0}` to `{1}`",
                range.Minimum,
                range.Maximum);

            Assert.Contains(phrase, row.Accepted, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The retention entry says what is deleted, what survives it, and that the
    /// deletion is permanent. An operator who reads a retention window as a
    /// display filter shortens it to tidy a page and loses the history.
    /// </summary>
    /// <remarks>
    /// This holds that the four statements are in the section and cannot hold
    /// that they are right. A section saying the wrong thing in the right words
    /// passes here and is caught by a reader.
    /// </remarks>
    [Fact]
    public void TheRetentionSectionSaysWhatGoesWhatStaysAndThatItIsPermanent()
    {
        var section = Section(RetentionHeading);

        Assert.Contains(nameof(PluginConfiguration.PlayRowRetentionDays), section, StringComparison.Ordinal);
        Assert.Contains(nameof(PluginConfiguration.DailyAggregateRetentionDays), section, StringComparison.Ordinal);
        Assert.Contains("There is no undo", section, StringComparison.Ordinal);
        Assert.Contains("What is deleted", section, StringComparison.Ordinal);
        Assert.Contains("What survives", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fields of the configuration model, as a caller of the type sees them.
    /// </summary>
    /// <remarks>
    /// Declared and inherited both, because a field the base class contributes is
    /// still a field the stored file carries and an operator can meet. Read-only
    /// properties are included for the same reason: the page reads RejectedFields
    /// and an operator sees the sentence it produces, so a reference that skipped
    /// it would leave the one field they are most likely to ask about undocumented.
    /// </remarks>
    /// <returns>The public instance properties of the configuration model.</returns>
    private static IEnumerable<PropertyInfo> Fields()
    {
        return typeof(PluginConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance);
    }

    /// <summary>
    /// Says what a default cell has to read for a given value.
    /// </summary>
    /// <remarks>
    /// One rule for every field rather than a cell-by-cell comparison, so a field
    /// of a type this has never seen fails loudly here instead of being compared
    /// against whatever ToString happens to produce.
    /// </remarks>
    /// <param name="value">The value a fresh model carries.</param>
    /// <returns>The text the entry has to name as the default.</returns>
    private static string Rendered(object? value)
    {
        switch (value)
        {
            case null:
                return Empty;
            case bool flag:
                return flag ? "true" : "false";
            case int number:
                return number.ToString(CultureInfo.InvariantCulture);
            case string text:
                return text.Length == 0 ? Empty : text;
            case IEnumerable list:
                var entries = list.Cast<object?>().Select(entry => entry?.ToString() ?? string.Empty).ToArray();
                return entries.Length == 0 ? Empty : string.Join(", ", entries);
            default:
                throw new Xunit.Sdk.XunitException(
                    "The configuration model carries a field of type " + value.GetType() + " and this check has no rule for writing its default down. Adding a field of a new type means deciding how the reference states its default.");
        }
    }

    /// <summary>
    /// Reads the table out of docs/configuration.md.
    /// </summary>
    /// <remarks>
    /// The four-column pipe table, with its heading row and its separator
    /// dropped. A document with no table fails rather than passing with nothing
    /// to compare, which is how a check over a document otherwise stops meaning
    /// anything the moment somebody reformats it.
    /// </remarks>
    /// <returns>The rows of the table.</returns>
    private static IReadOnlyList<Row> Table()
    {
        var rows = File.ReadAllLines(Reference())
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length == 4)
            .Where(cells => !cells[0].StartsWith("---", StringComparison.Ordinal))
            .Where(cells => !string.Equals(cells[0], "setting", StringComparison.Ordinal))
            .Select(cells => new Row(Bare(cells[0]), Bare(cells[1]), cells[2], cells[3]))
            .ToList();

        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>
    /// Reads one section of the reference, by its heading.
    /// </summary>
    /// <param name="heading">The heading text, without its marker.</param>
    /// <returns>The text of that section, up to the next heading.</returns>
    private static string Section(string heading)
    {
        var lines = File.ReadAllLines(Reference());
        var start = Array.FindIndex(lines, line => string.Equals(line.Trim(), "## " + heading, StringComparison.Ordinal));

        Assert.True(start >= 0, "The reference carries no section headed \"" + heading + "\".");

        var rest = lines.Skip(start + 1).ToArray();
        var end = Array.FindIndex(rest, line => line.StartsWith("## ", StringComparison.Ordinal));

        return string.Join("\n", end < 0 ? rest : rest.Take(end));
    }

    /// <summary>
    /// Takes the code markers off a cell, so the document can mark a value up
    /// without the comparison having to know about it.
    /// </summary>
    /// <param name="cell">The cell as it is written.</param>
    /// <returns>The cell's text.</returns>
    private static string Bare(string cell) => cell.Replace("`", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>
    /// Finds the reference document.
    /// </summary>
    /// <returns>The full path of docs/configuration.md.</returns>
    private static string Reference()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.yaml")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "No build.yaml was found above " + AppContext.BaseDirectory + ".");

        var path = Path.Combine(directory!.FullName, "docs", "configuration.md");
        Assert.True(File.Exists(path), "The reference this checks is missing: " + path);

        return path;
    }
}
