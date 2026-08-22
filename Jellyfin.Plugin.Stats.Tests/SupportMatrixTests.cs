using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// docs/support-matrix.md tells somebody with an older server which artifact is
/// theirs. Every number in it is written somewhere else first: the floors in
/// Directory.Build.props, the frameworks in the plugin project, one abi in
/// build.yaml and the other in the packaging workflow, and the version in
/// build.yaml. A table typed out beside those is a table that is right on the
/// day it is written, and the failure it produces is somebody installing an
/// artifact their server cannot load.
/// <para>
/// So each cell is compared against the file the build reads it from. What this
/// cannot check is whether the table is the right shape or says the right thing
/// in prose; it checks that no cell disagrees with the tree. Issue #79.
/// </para>
/// </summary>
public class SupportMatrixTests
{
    /// <summary>
    /// The cell every unreleased row carries, and the version in build.yaml that
    /// makes it true.
    /// </summary>
    private const string NoneReleased = "none released";

    /// <summary>
    /// The one line ending every pattern here is written against.
    /// </summary>
    /// <remarks>
    /// A character rather than an escape in a literal, because this file is one
    /// of the tracked files whose line endings are the thing in question, and a
    /// literal carrying the byte it is about is a literal a clone can rewrite
    /// under it.
    /// </remarks>
    private static readonly string LineFeed = ((char)10).ToString();

    /// <summary>
    /// Where the document writes the version that is waiting for its first tag.
    /// </summary>
    /// <remarks>
    /// This was the literal <c>0.0.0.0</c>, on the reading that the version in
    /// <c>build.yaml</c> moves when a release is cut. It does not. Issue #133
    /// settled the sequence as raising the number first and tagging second,
    /// because a release deleted to correct its number burns that tag
    /// permanently, so there is a window in which the file names a version that
    /// nothing has published. Pinned to the old literal, this check refused
    /// exactly the first step of that sequence.
    /// <para>
    /// What it compares instead is the file against the number the document
    /// declares is awaiting a tag, which is the same shape of statement as
    /// every other row here: two written numbers that have to agree. It keeps
    /// the force the old constant had, because the rows still say nothing has
    /// been released and the comparison still has to be brought along when that
    /// stops being true.
    /// </para>
    /// </remarks>
    private const string AwaitingItsFirstTag = "(?m)^    awaiting its first tag:[ ]*(?<value>[0-9.]+)$";

    /// <summary>
    /// A row of the table, under the framework it is about.
    /// </summary>
    /// <param name="Line">The server line.</param>
    /// <param name="Framework">The framework the artifact for that line targets.</param>
    /// <param name="Floor">The oldest server release the artifact is built against.</param>
    /// <param name="TargetAbi">The abi the package for that line declares.</param>
    /// <param name="PluginVersions">The plugin versions the row is about.</param>
    private sealed record Row(string Line, string Framework, string Floor, string TargetAbi, string PluginVersions);

    /// <summary>
    /// The table lists a row for each framework the plugin builds and for no
    /// other. A third line added to the build without a row here, or a row for a
    /// line the plugin stopped shipping, is the drift this whole file is about.
    /// </summary>
    [Fact]
    public void TheTableCoversExactlyTheFrameworksThePluginBuilds()
    {
        var built = ProjectProperty(
                Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.Stats", "Jellyfin.Plugin.Stats.csproj"),
                "TargetFrameworks")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(framework => framework, StringComparer.Ordinal);

        var tabled = Table().Select(row => row.Framework).OrderBy(framework => framework, StringComparer.Ordinal);

        Assert.Equal(built, tabled);
    }

    /// <summary>
    /// Every floor in the table is the floor the build compiles that line
    /// against. The properties are read out of Directory.Build.props, which is
    /// the file MSBuild reads them from and the only place they are written.
    /// </summary>
    [Fact]
    public void EveryFloorInTheTableIsTheFloorTheBuildUses()
    {
        var props = Path.Combine(RepositoryRoot(), "Directory.Build.props");

        foreach (var row in Table())
        {
            var property = FloorPropertyFor(row.Framework);
            Assert.Equal(ProjectProperty(props, property), row.Floor);
        }
    }

    /// <summary>
    /// The abi a package declares is what a server compares its own version
    /// against before offering the plugin, so a wrong one here is an install on a
    /// server that cannot load the assembly. The 10.11 line's is written in
    /// build.yaml; the 12.0 line's is not a value in the tree at all, because the
    /// packaging workflow substitutes it into a copy of build.yaml, so that one
    /// is read from the workflow that writes it.
    /// </summary>
    [Fact]
    public void EveryTargetAbiInTheTableIsTheOneItsPackageDeclares()
    {
        var declared = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["net9.0"] = Captured(
                File.ReadAllText(Path.Combine(RepositoryRoot(), "build.yaml")),
                "(?m)^targetAbi:[ ]*\"(?<value>[^\"]*)\"",
                "build.yaml"),
            ["net10.0"] = Captured(
                File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "package.yml")),
                "\\(\"targetAbi\",[ ]*\"(?<value>[^\"]*)\"\\)",
                "the packaging workflow")
        };

        foreach (var row in Table())
        {
            Assert.True(
                declared.TryGetValue(row.Framework, out var expected),
                "The table has a row for " + row.Framework + " and nothing here reads the abi that line's package declares. Adding a line to the build means adding where its abi comes from, not only a row.");

            Assert.Equal(expected, row.TargetAbi);
        }
    }

    /// <summary>
    /// The plugin versions column stays honest across the first release. While
    /// build.yaml carries the unreleased version, every row says so; the moment
    /// it does not, a row still saying so fails here and the table has to be
    /// brought along with the release.
    /// </summary>
    [Fact]
    public void ThePluginVersionsColumnAgreesWithTheVersionTheBuildCarries()
    {
        var version = Captured(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "build.yaml")),
            "(?m)^version:[ ]*\"(?<value>[^\"]*)\"",
            "build.yaml");

        if (Table().Any(row => string.Equals(row.PluginVersions, NoneReleased, StringComparison.Ordinal)))
        {
            var awaiting = Captured(
                File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "support-matrix.md")),
                AwaitingItsFirstTag,
                "the plugin versions section of docs/support-matrix.md");

            Assert.Equal(awaiting, version);
        }
    }

    /// <summary>
    /// The one check that reads the artifact rather than a file the artifact was
    /// built from. Everything above compares one written number against another;
    /// this asks the compiled plugin which Jellyfin it was actually built
    /// against, and compares that to the row for the framework the suite is
    /// running on. A project that quietly resolved a different version passes
    /// every other check here and fails this one.
    /// </summary>
    /// <remarks>
    /// The package is called Jellyfin.Controller and the assembly inside it is
    /// called MediaBrowser.Controller, so the reference is looked up under the
    /// second name. The assembly version also carries no pre-release part, so a
    /// floor of 12.0.0-rc1 is compared as 12.0.0. That is the bound: this proves
    /// the release the artifact was built against and not which candidate of it.
    /// </remarks>
    [Fact]
    public void ThePluginIsCompiledAgainstTheFloorTheTableClaims()
    {
        var framework = RunningFramework();

        var row = Table().SingleOrDefault(candidate => string.Equals(candidate.Framework, framework, StringComparison.Ordinal));
        Assert.True(row is not null, "The table has no row for " + framework + ", which is the framework this suite is running on.");

        var reference = typeof(Plugin).Assembly.GetReferencedAssemblies()
            .SingleOrDefault(assembly => string.Equals(assembly.Name, "MediaBrowser.Controller", StringComparison.Ordinal));
        Assert.True(reference is not null, "The plugin assembly references no MediaBrowser.Controller, which is the assembly the Jellyfin.Controller package carries, so there is nothing to compare the floor against.");

        var claimed = row!.Floor.Split('-', 2)[0];
        var built = reference!.Version!;

        Assert.Equal(
            claimed,
            built.Major + "." + built.Minor + "." + built.Build);
    }

    /// <summary>
    /// Reads the table out of docs/support-matrix.md.
    /// </summary>
    /// <remarks>
    /// The first pipe table in the document, with its heading row and its
    /// separator dropped. A document with no table fails here rather than
    /// passing with nothing to compare, which is the way this file would
    /// otherwise stop meaning anything.
    /// </remarks>
    /// <returns>The rows of the table.</returns>
    private static IReadOnlyList<Row> Table()
    {
        var document = File.ReadAllLines(Path.Combine(RepositoryRoot(), "docs", "support-matrix.md"));

        var rows = document
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('|'))
            .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .Where(cells => cells.Length == 5)
            .Where(cells => !cells[0].StartsWith("---", StringComparison.Ordinal))
            .Where(cells => !string.Equals(cells[0], "server line", StringComparison.Ordinal))
            .Select(cells => new Row(cells[0], cells[1], cells[2], cells[3], cells[4]))
            .ToList();

        Assert.NotEmpty(rows);
        return rows;
    }

    /// <summary>
    /// Names the property holding the floor for a framework.
    /// </summary>
    /// <param name="framework">The framework a row is about.</param>
    /// <returns>The MSBuild property that declares that framework's floor.</returns>
    private static string FloorPropertyFor(string framework)
    {
        return framework switch
        {
            "net9.0" => "JellyfinFloorNet9",
            "net10.0" => "JellyfinFloorNet10",
            _ => throw new Xunit.Sdk.XunitException(
                "The table has a row for " + framework + " and no floor property is named for it. A framework added to the build needs its floor declared before this table can be checked against it.")
        };
    }

    /// <summary>
    /// Reads a single-valued property out of an MSBuild file.
    /// </summary>
    /// <remarks>
    /// The file the build reads is read, rather than a copy of the numbers kept
    /// beside it. A property written twice fails rather than returning whichever
    /// one is first, because two declarations of a floor is the state where the
    /// build and this check can disagree.
    /// </remarks>
    /// <param name="path">The MSBuild file to read.</param>
    /// <param name="property">The property to read out of it.</param>
    /// <returns>The value of the property.</returns>
    private static string ProjectProperty(string path, string property)
    {
        var matches = Regex.Matches(
            File.ReadAllText(path),
            "<" + Regex.Escape(property) + "\\s*>(?<value>[^<]*)</" + Regex.Escape(property) + ">");

        Assert.True(
            matches.Count == 1,
            path + " declares " + property + " " + matches.Count + " time(s), and this check reads one.");

        return matches[0].Groups["value"].Value.Trim();
    }

    /// <summary>
    /// Reads one capture out of a file's text.
    /// </summary>
    /// <remarks>
    /// The line endings are levelled first, and that is the difference between
    /// a check anybody can run and one only the runner can run. This repository
    /// has no attributes file, so git decides per clone how a tracked document
    /// is written to disk, and a pattern anchored at the end of a line meets a
    /// carriage return on a clone that took the other answer. In .NET a dollar
    /// in multiline mode matches before the newline and never before the
    /// carriage return, so the capture cannot reach its anchor and the check
    /// refuses a document that says exactly what it is supposed to say. The
    /// message it refuses with names the document, which sends a reader to edit
    /// the line the pattern is looking at.
    /// <para>
    /// What it costs is that a difference in line endings alone walks through
    /// here. That is the same weakening the consent wording's fingerprint took
    /// on issue #42 and states beside itself, and no cell of this table is
    /// about a line ending: every one of them is a version, a framework or an
    /// abi.
    /// </para>
    /// <para>
    /// The wider answer is for this repository to declare its line endings once
    /// so that no check depends on which clone it runs in. That reaches every
    /// tracked file and every reader of one, so it is a decision rather than a
    /// repair, and it is not taken here. Issue #133.
    /// </para>
    /// </remarks>
    /// <param name="text">The text to read.</param>
    /// <param name="pattern">A pattern with a group named value.</param>
    /// <param name="what">What is being read, for the failure message.</param>
    /// <returns>The captured value.</returns>
    private static string Captured(string text, string pattern, string what)
    {
        var matches = Regex.Matches(text.ReplaceLineEndings(LineFeed), pattern);

        Assert.True(
            matches.Count == 1,
            what + " carries " + matches.Count + " value(s) matching " + pattern + ", and this check reads one.");

        return matches[0].Groups["value"].Value;
    }

    /// <summary>
    /// Names the framework the suite is currently running on.
    /// </summary>
    /// <remarks>
    /// Read off the attribute the compiler stamps into this assembly, so it is
    /// the framework this run was built for rather than the newest runtime the
    /// machine happens to have. The suite builds once per framework the plugin
    /// ships on, so both rows of the table are reached across the two runs and
    /// neither is reached twice.
    /// </remarks>
    /// <returns>The framework moniker, in the form the project files use.</returns>
    private static string RunningFramework()
    {
        var attribute = typeof(SupportMatrixTests).Assembly
            .GetCustomAttributes(typeof(System.Runtime.Versioning.TargetFrameworkAttribute), false)
            .Cast<System.Runtime.Versioning.TargetFrameworkAttribute>()
            .SingleOrDefault();

        Assert.True(attribute is not null, "The test assembly carries no target framework attribute.");

        var declared = Regex.Match(attribute!.FrameworkName, @"^\.NETCoreApp,Version=v(?<version>[0-9]+\.[0-9]+)$");
        Assert.True(declared.Success, "The target framework attribute reads " + attribute.FrameworkName + ", which this check cannot turn into a moniker.");

        return "net" + declared.Groups["version"].Value;
    }

    /// <summary>
    /// Finds the directory holding the tracked build.yaml.
    /// </summary>
    /// <returns>The full path of the directory that holds build.yaml.</returns>
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
