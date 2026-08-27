using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// A plugin page is served by the server to anybody who asks for it. The endpoint
/// listing configuration pages carries an elevation policy and the endpoint
/// returning a page's content carries no authorization attribute, and the server
/// configures no fallback policy, so an action with no attribute is reachable
/// without credentials. Whatever a page asset contains is therefore public, and
/// the only safe page is one that contains markup and code and nothing else.
/// <para>
/// This suite reads the compiled assembly rather than the files beside it,
/// because the embedded copy is the one a server serves. Three properties are
/// asserted over it: the embedded asset is exactly what its tracked source
/// produces, so nothing is substituted into it while it is built; it carries no
/// marker for a value a server would fill in; and the only identifier in it is
/// the plugin's own. The first two are what "no value is embedded at build time"
/// means in a form a machine can refuse, and the third is what a leaked user or
/// item would look like.
/// <para>
/// THERE ARE TWO KINDS OF PAGE HERE AND THE FIRST PROPERTY MEANS SOMETHING
/// DIFFERENT FOR EACH. A page written as a page is embedded byte for byte, which
/// is what it has always meant. A page assembled from the drawing modules is
/// embedded as its tracked template with the modules laid into the template's
/// empty script element, and what is refused there is any difference from that
/// assembly - so the bytes still come from tracked files and nothing else, and
/// the build cannot put a value into a page any more than it could before. Issue
/// #67 is where the assembly was decided and why: every plugin page is served
/// from one address with the page named in the query, so the relative imports
/// between the modules find nothing, and a page that carried its code by hand
/// would put the modules out of reach of the suite that drives them.
/// </para>
/// <para>
/// WHAT THE MARKER CHECK READS MOVED WITH IT, AND THIS IS THE PART TO READ
/// TWICE. It reads the tracked source a page was produced from rather than the
/// produced page: the tracked file for a page written as one, and the template
/// for an assembled one. The modules are not scanned by it, because they carry
/// nineteen JSDoc type annotations of the shape <c>{{a: string}}</c> that the
/// marker pattern matches and that are not markers - they were there before any
/// of this and no route read them, since both halves of that rule read
/// <c>.html</c>. What stands in their place is the property above: every byte
/// inside the script element came from a tracked module, so the build cannot
/// have filled anything in, and what a module says is what the review is for.
/// </para>
/// </para>
/// <para>
/// What this cannot see is a value somebody typed into a page by hand, because
/// a literal in a committed file and ordinary page text are the same bytes.
/// <c>no-server-value-in-a-page-asset</c> in the greppable invariants reads the
/// tracked file for the same markers and is the half that runs before a build
/// exists; neither half judges prose. Issue #63.
/// </para>
/// </summary>
public class PageAssetTests
{
    /// <summary>
    /// The marker shapes a page carries when a server, a template engine or a
    /// build step is expected to fill something in before the page is served.
    /// The same expression is written as a rule in tools/invariants/rules, and
    /// the two are kept the same shape deliberately: the rule reads the tracked
    /// file, this reads what was built out of it.
    /// </summary>
    private const string SubstitutionMarker =
        @"\{\{[^}]*\}\}|<%[^>]*%>|@(Model|ViewBag|ViewData|Html)\b|%%[A-Za-z_][A-Za-z0-9_]*%%";

    /// <summary>
    /// Any identifier of the shape the server hands out for a user, an item, a
    /// device or a plugin.
    /// </summary>
    private const string AnyIdentifier =
        "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}";

    [Fact]
    public void EveryPageThePluginDeclaresIsEmbeddedUnderTheNameItDeclares()
    {
        var embedded = typeof(Plugin).Assembly.GetManifestResourceNames();

        foreach (var page in Pages())
        {
            Assert.Contains(page.EmbeddedResourcePath, embedded);
        }
    }

    /// <summary>
    /// A page asset in the project that never reaches the assembly is a page
    /// nobody can serve, and an embedded asset with no tracked source is one
    /// nobody reviewed. Either way the set this suite walks would be the wrong
    /// set, so it is compared rather than assumed, and a new page joins these
    /// checks by existing rather than by somebody remembering.
    /// </summary>
    [Fact]
    public void TheEmbeddedPageAssetsAreExactlyTheTrackedOnes()
    {
        var embedded = EmbeddedPageAssets().Keys.OrderBy(name => name, StringComparer.Ordinal);
        var expected = TrackedPageAssets().Keys
            .Concat(AssembledPages().Keys)
            .OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(expected, embedded);
    }

    /// <summary>
    /// Proves that nothing is written into a page while it is built. A token
    /// replacement added to the project file, or a generator that rewrites the
    /// markup on the way in, moves the embedded bytes away from the reviewed
    /// ones and fails here.
    /// </summary>
    [Fact]
    public void EveryEmbeddedPageAssetIsByteForByteTheTrackedFile()
    {
        var tracked = TrackedPageAssets();

        foreach (var asset in EmbeddedPageAssets())
        {
            if (AssembledPages().ContainsKey(asset.Key))
            {
                continue;
            }

            Assert.True(
                tracked.TryGetValue(asset.Key, out var path),
                "The assembly embeds " + asset.Key + ", and no tracked file under the plugin project produces that name.");

            Assert.True(
                File.ReadAllBytes(path!).SequenceEqual(asset.Value),
                "The embedded copy of " + asset.Key + " differs from " + path + ". A page asset is served exactly as it is committed, so a difference is either a build step writing into it or a build that is out of date.");
        }
    }

    /// <summary>
    /// The same property for a page the build assembles: it is its tracked
    /// template with the modules it names laid into the template's empty script
    /// element, and nothing else.
    /// </summary>
    /// <remarks>
    /// The comparison is exact, so the rule written in the project file and the
    /// rule written here cannot silently disagree: they produce the same bytes
    /// or this fails. That is what buys the assembly, and it is why the rule is
    /// deliberately small - drop the imports, which name files that are no
    /// longer separate, and drop the word that exported what is now in scope.
    /// <para>
    /// What the build is declared to assemble is read out of the project file
    /// rather than listed here. A page added there is covered by arriving, and a
    /// list in this file would be the thing that goes stale against the build it
    /// is about.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryAssembledPageIsItsTemplateWithItsOwnModulesInIt()
    {
        var embedded = EmbeddedPageAssets();
        var assembled = AssembledPages();

        Assert.NotEmpty(assembled);

        foreach (var page in assembled)
        {
            Assert.True(
                embedded.TryGetValue(page.Key, out var bytes),
                "The project declares that " + page.Key + " is assembled, and the assembly embeds no resource under that name.");

            Assert.Equal(Assemble(page.Value.Template, page.Value.Modules), Text(bytes!).Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Every module the build lays into a page is a tracked file under the
    /// plugin project, and every one of them reaches the page.
    /// </summary>
    /// <remarks>
    /// The case above compares the page against an assembly of the files the
    /// project names, so a project naming no modules at all would produce a page
    /// with an empty script element and pass. This is the other direction.
    /// </remarks>
    [Fact]
    public void EveryModuleAPageIsAssembledFromIsTrackedAndReachesIt()
    {
        foreach (var page in AssembledPages())
        {
            Assert.NotEmpty(page.Value.Modules);

            var text = Text(EmbeddedPageAssets()[page.Key]);

            foreach (var module in page.Value.Modules)
            {
                Assert.True(File.Exists(module), "The project assembles " + page.Key + " from " + module + ", which is not a file under the plugin project.");

                // A declaration is only worth something if the module reaches
                // the page, so a line of it is looked for rather than its name.
                // The last thing a module declares is the one furthest from the
                // top of the file, which is where a truncated read would stop.
                var last = File.ReadAllText(module)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Split('\n')
                    .Where(line => line.StartsWith("export function ", StringComparison.Ordinal))
                    .Select(line => line["export ".Length..])
                    .LastOrDefault();

                Assert.True(last is not null, module + " declares no function, so nothing in it could reach a page.");
                Assert.Contains(last!, text, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// No page carries a marker for a value a server, a template engine or a
    /// build step would fill in.
    /// </summary>
    /// <remarks>
    /// Read over the tracked source each page is produced from: the file itself
    /// for a page written as one, and the template for an assembled one. The
    /// class remark above carries why the modules are outside this and what
    /// holds in their place.
    /// </remarks>
    [Fact]
    public void NoEmbeddedPageAssetCarriesAMarkerForAServerSideValue()
    {
        var sources = TrackedPageAssets()
            .Select(asset => (asset.Key, Text(File.ReadAllBytes(asset.Value))))
            .Concat(AssembledPages().Select(page => (page.Key, Text(File.ReadAllBytes(page.Value.Template)))));

        foreach (var (name, text) in sources)
        {
            var found = Regex.Matches(text, SubstitutionMarker)
                .Select(match => match.Value)
                .ToArray();

            Assert.True(
                found.Length == 0,
                name + " carries " + found.Length + " substitution marker(s): " + string.Join(", ", found) + ". A page is served to an unauthenticated caller, so every value it shows is fetched after it loads and none is filled in for it.");
        }
    }

    /// <summary>
    /// A user, an item and a device are identified by the same shape, so a page
    /// that leaked any of them would carry one of these. The plugin's own
    /// identifier is in the page legitimately, because the page passes it to the
    /// two configuration calls it makes, and it is public in build.yaml anyway.
    /// Every other one is a value the server knows and the page may not.
    /// </summary>
    [Fact]
    public void TheOnlyIdentifierInAPageAssetIsThePluginsOwn()
    {
        var own = PluginId();

        foreach (var asset in EmbeddedPageAssets())
        {
            foreach (Match match in Regex.Matches(Text(asset.Value), AnyIdentifier))
            {
                Assert.True(
                    Guid.TryParse(match.Value, out var found) && found == own,
                    asset.Key + " carries the identifier " + match.Value + ", which is not this plugin's. A page asset is public, so an identifier in one is a user, an item or a device named to anybody who asks for the page.");
            }
        }
    }

    /// <summary>
    /// Reads the pages the plugin declares to the server.
    /// </summary>
    /// <remarks>
    /// <see cref="Plugin.GetPages"/> reads no instance state, so the object is
    /// made without running the constructor, which needs server surfaces this
    /// test has no reason to fake. A page list that later starts reading state
    /// fails here rather than passing quietly.
    /// </remarks>
    /// <returns>The page declarations the plugin returns.</returns>
    private static IEnumerable<MediaBrowser.Model.Plugins.PluginPageInfo> Pages()
    {
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        return plugin.GetPages();
    }

    /// <summary>
    /// Reads the plugin's own identifier off the compiled type.
    /// </summary>
    /// <returns>The value of the plugin's identifier property.</returns>
    private static Guid PluginId()
    {
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
        return plugin.Id;
    }

    /// <summary>
    /// Reads every page asset out of the compiled plugin assembly.
    /// </summary>
    /// <returns>The bytes of each embedded page asset, by resource name.</returns>
    private static IReadOnlyDictionary<string, byte[]> EmbeddedPageAssets()
    {
        var assembly = typeof(Plugin).Assembly;
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var name in assembly.GetManifestResourceNames().Where(IsPageAsset))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            Assert.True(stream is not null, "The assembly lists a resource named " + name + " and returns no stream for it.");

            using var buffer = new MemoryStream();
            stream!.CopyTo(buffer);
            assets[name] = buffer.ToArray();
        }

        Assert.NotEmpty(assets);
        return assets;
    }

    /// <summary>
    /// Finds the page assets tracked under the plugin project, under the resource
    /// name each one is embedded as.
    /// </summary>
    /// <remarks>
    /// The name is built the way the SDK builds it, by joining the root namespace
    /// to the path of the file with the separators turned into dots. Build output
    /// is skipped, because a copy of a page under bin is the same page counted
    /// twice.
    /// </remarks>
    /// <returns>The path of each tracked page asset, by resource name.</returns>
    private static IReadOnlyDictionary<string, string> TrackedPageAssets()
    {
        var project = Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.Stats");
        var root = typeof(Plugin).Namespace;
        var tracked = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories).Where(IsPageAsset))
        {
            var relative = Path.GetRelativePath(project, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment.Equals("bin", StringComparison.Ordinal) || segment.Equals("obj", StringComparison.Ordinal)))
            {
                continue;
            }

            tracked[root + "." + relative.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.')] = file;
        }

        Assert.NotEmpty(tracked);
        return tracked;
    }

    /// <summary>
    /// Decides whether a name is one of the page assets these checks are about.
    /// </summary>
    /// <remarks>
    /// A template is not one. It is tracked source that no build embeds, and
    /// counting it as a page would have the set comparison above looking for an
    /// embedded resource that is not meant to exist.
    /// </remarks>
    /// <param name="name">A resource name or a file path.</param>
    /// <returns>True where the name is a page asset.</returns>
    private static bool IsPageAsset(string name)
    {
        return name.EndsWith(".html", StringComparison.Ordinal)
               && !name.EndsWith(".template.html", StringComparison.Ordinal);
    }

    /// <summary>
    /// The pages the project declares that the build assembles, by the resource
    /// name each is embedded under.
    /// </summary>
    /// <remarks>
    /// Read out of the project file rather than listed here, so what this suite
    /// checks is what the build was told to do. A page added to the project is
    /// covered by arriving.
    /// </remarks>
    /// <returns>The template and the modules of each assembled page.</returns>
    private static IReadOnlyDictionary<string, (string Template, IReadOnlyList<string> Modules)> AssembledPages()
    {
        var project = Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.Stats");
        var text = File.ReadAllText(Path.Combine(project, "Jellyfin.Plugin.Stats.csproj"));
        var declared = new Dictionary<string, (string, IReadOnlyList<string>)>(StringComparer.Ordinal);

        foreach (Match call in Regex.Matches(text, "<AssembleAPage\\s+Template=\"(?<template>[^\"]+)\"\\s+Modules=\"(?<modules>[^\"]+)\"\\s+Destination=\"(?<destination>[^\"]+)\""))
        {
            var page = Path.GetFileName(call.Groups["destination"].Value.Replace('\\', Path.DirectorySeparatorChar));

            declared[typeof(Plugin).Namespace + ".Pages." + page] = (
                Path.Combine(project, call.Groups["template"].Value.Replace('\\', Path.DirectorySeparatorChar)),
                call.Groups["modules"].Value
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(module => Path.Combine(project, module.Replace('\\', Path.DirectorySeparatorChar)))
                    .ToList());
        }

        return declared;
    }

    /// <summary>
    /// Lays a page's modules into its template, the way the build does.
    /// </summary>
    /// <remarks>
    /// The same rule as the one in <c>Pages/pages.targets</c>, written a second
    /// time on purpose and compared exactly against the first, so the two cannot
    /// disagree without this failing.
    /// </remarks>
    /// <param name="template">The tracked template.</param>
    /// <param name="modules">The modules it names, in order.</param>
    /// <returns>The page.</returns>
    private static string Assemble(string template, IReadOnlyList<string> modules)
    {
        const string Slot = "<script id=\"stats-page-code\"></script>";

        var code = new System.Text.StringBuilder();

        foreach (var module in modules)
        {
            foreach (var line in File.ReadAllText(module).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (line.StartsWith("import ", StringComparison.Ordinal))
                {
                    continue;
                }

                code.Append(line.StartsWith("export ", StringComparison.Ordinal) ? line["export ".Length..] : line);
                code.Append('\n');
            }
        }

        return File.ReadAllText(template)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace(Slot, "<script id=\"stats-page-code\">\n" + code + "</script>", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads an asset's bytes as text, honouring a byte order mark if one is there.
    /// </summary>
    /// <param name="bytes">The bytes of the asset.</param>
    /// <returns>The text of the asset.</returns>
    private static string Text(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Finds the directory holding the tracked build.yaml.
    /// </summary>
    /// <remarks>
    /// The suite runs out of its own bin directory, so the walk climbs until it
    /// finds the file and gives up at the top of the volume. The same walk as
    /// <see cref="PluginIdentityTests"/> makes, for the same reason.
    /// </remarks>
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
