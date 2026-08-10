using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// docs/what-is-stored.md is the account an administrator reads instead of the
/// source, so a claim in it that has stopped being true is worse than no
/// document: it is a promise about somebody else's data, made in the one place
/// a reader has no way to check.
/// <para>
/// Three of its claims are checkable here and are checked. The field table is
/// compared against the statement the store runs, so a column added to the
/// schema without an entry is red. Every invariant rule the document leans on is
/// looked up in the rules file, so a rule renamed or removed takes the sentence
/// citing it with it. Every setting it names is looked up on the configuration
/// model, which is the third condition of issue #77 in the direction that
/// matters: a document describing a control the plugin does not have.
/// </para>
/// <para>
/// What none of this holds is whether the prose is true. Nothing here judges
/// whether a column's description says what the column means, and a wrong
/// sentence in the right shape passes. That is what a reader is for.
/// </para>
/// </summary>
public class WhatIsStoredTests
{
    /// <summary>
    /// The heading of the section whose table lists the stored columns.
    /// </summary>
    private const string ColumnHeading = "One row per finished play";

    /// <summary>
    /// The heading of the section that names the settings reaching the data.
    /// </summary>
    private const string ControlHeading = "What an administrator controls";

    /// <summary>
    /// The table lists exactly the columns the store creates. A column added to
    /// the schema and not to the table leaves the document claiming a complete
    /// account while holding an incomplete one, and an entry left behind by a
    /// column that went describes a field nobody's server holds.
    /// </summary>
    [Fact]
    public void TheTableListsExactlyTheColumnsTheSchemaCreates()
    {
        var schema = ColumnsTheSchemaCreates().OrderBy(name => name, StringComparer.Ordinal);
        var documented = DocumentedColumns().OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(schema, documented);
    }

    /// <summary>
    /// Every invariant rule the document names is a rule that exists. The
    /// document's answers to who can read the data and what is refused on
    /// purpose both rest on a greppable rule doing the refusing, and a rule id
    /// that no longer resolves turns those paragraphs into assurances with
    /// nothing behind them.
    /// </summary>
    [Fact]
    public void EveryRuleTheDocumentNamesIsARuleThatExists()
    {
        // No end anchor. A checkout on a machine that writes CRLF leaves a
        // carriage return where the anchor expects the line to end, and the set
        // would come back empty on that machine and full on another.
        var declared = Ids(File.ReadAllText(RulesFile()), @"^id:\s*([^\s]+)");
        var cited = Ids(Document(), @"`(no-[a-z0-9-]+)`");

        Assert.NotEmpty(cited);

        foreach (var rule in cited)
        {
            Assert.True(
                declared.Contains(rule),
                "The document cites the invariant rule " + rule + ", which is not in " + RulesFile() + ".");
        }
    }

    /// <summary>
    /// Every setting the document says reaches the data is a field on the
    /// configuration model. This is the third condition of the issue read in the
    /// direction a document actually fails in: not a control invented out of
    /// nothing, but one renamed underneath a paragraph that goes on naming it.
    /// </summary>
    [Fact]
    public void EverySettingTheDocumentNamesIsOnTheConfigurationModel()
    {
        var fields = typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        var named = Ids(Section(ControlHeading), @"`([A-Z][A-Za-z]+)`");

        Assert.NotEmpty(named);

        foreach (var setting in named)
        {
            Assert.True(
                fields.Contains(setting),
                "The document describes a setting called " + setting + ", which the configuration model does not have.");
        }
    }

    /// <summary>
    /// Every file the document points at is a file that is there. A document
    /// whose every claim points at the behaviour implementing it is worth
    /// exactly as much as those pointers resolve.
    /// </summary>
    [Fact]
    public void EveryPathTheDocumentNamesIsAPathThatExists()
    {
        var named = Ids(Document(), @"`((?:docs|tools|Jellyfin\.Plugin\.Stats(?:\.Tests)?)/[A-Za-z0-9./_-]+)`");

        Assert.NotEmpty(named);

        foreach (var path in named)
        {
            var full = Path.Combine(Root(), path.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(
                File.Exists(full) || Directory.Exists(full),
                "The document points at " + path + ", which is not in the tree.");
        }
    }

    /// <summary>
    /// The column names out of the statement the store runs to create its table.
    /// </summary>
    /// <remarks>
    /// Read off <see cref="SchemaMigrations"/> rather than off
    /// <see cref="PlayRecord"/>, because the record is the shape in memory and
    /// the table is what is on an administrator's disk. The two differ on
    /// purpose: one record property becomes eight columns, and a document
    /// compared against the record would list fields no file holds.
    /// </remarks>
    /// <returns>The column names, as the create statement spells them.</returns>
    private static IEnumerable<string> ColumnsTheSchemaCreates()
    {
        var create = SchemaMigrations.All
            .SelectMany(step => step.Statements)
            .SingleOrDefault(statement => statement.Contains("CREATE TABLE", StringComparison.Ordinal));

        Assert.True(create is not null, "No step in the schema creates a table, so there is nothing to compare the document against.");

        var body = create![(create.IndexOf('(', StringComparison.Ordinal) + 1)..create.LastIndexOf(')')];

        return body
            .Split(',')
            .Select(column => column.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();
    }

    /// <summary>
    /// The first cell of every row of the table under the column heading.
    /// </summary>
    /// <returns>The column names the document claims.</returns>
    private static IReadOnlyList<string> DocumentedColumns()
    {
        var rows = Section(ColumnHeading)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|')[0].Trim())
            .Where(cell => cell.StartsWith('`') && cell.EndsWith('`'))
            .Select(cell => cell.Trim('`'))
            .ToList();

        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>
    /// Pulls the first capture of every match out of some text, without repeats.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="pattern">The pattern, whose first group is what is wanted.</param>
    /// <returns>What was captured, each value once.</returns>
    private static IReadOnlySet<string> Ids(string text, string pattern)
    {
        return Regex
            .Matches(text, pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads one section of the document, by its heading.
    /// </summary>
    /// <param name="heading">The heading text, without its marker.</param>
    /// <returns>The text of that section, up to the next heading.</returns>
    private static string Section(string heading)
    {
        var lines = Document().Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var start = Array.FindIndex(lines, line => string.Equals(line.Trim(), "## " + heading, StringComparison.Ordinal));

        Assert.True(start >= 0, "The document carries no section headed \"" + heading + "\".");

        var rest = lines.Skip(start + 1).ToArray();
        var end = Array.FindIndex(rest, line => line.StartsWith("## ", StringComparison.Ordinal));

        return string.Join("\n", end < 0 ? rest : rest.Take(end));
    }

    /// <summary>
    /// The document this file is about.
    /// </summary>
    /// <returns>Its text.</returns>
    private static string Document() => File.ReadAllText(Path.Combine(Root(), "docs", "what-is-stored.md"));

    /// <summary>
    /// The invariant rules file the document cites.
    /// </summary>
    /// <returns>Its full path.</returns>
    private static string RulesFile() => Path.Combine(Root(), "tools", "invariants", "rules");

    /// <summary>
    /// Finds the top of the working tree from wherever the suite was built to.
    /// </summary>
    /// <returns>The directory holding build.yaml.</returns>
    private static string Root()
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
