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
/// asserted over it: the embedded asset is byte for byte the tracked file, so
/// nothing is substituted into it while it is built; it carries no marker for a
/// value a server would fill in; and the only identifier in it is the plugin's
/// own. The first two are what "no value is embedded at build time" means in a
/// form a machine can refuse, and the third is what a leaked user or item would
/// look like.
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
        var tracked = TrackedPageAssets().Keys.OrderBy(name => name, StringComparer.Ordinal);

        Assert.Equal(tracked, embedded);
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
            Assert.True(
                tracked.TryGetValue(asset.Key, out var path),
                "The assembly embeds " + asset.Key + ", and no tracked file under the plugin project produces that name.");

            Assert.True(
                File.ReadAllBytes(path!).SequenceEqual(asset.Value),
                "The embedded copy of " + asset.Key + " differs from " + path + ". A page asset is served exactly as it is committed, so a difference is either a build step writing into it or a build that is out of date.");
        }
    }

    [Fact]
    public void NoEmbeddedPageAssetCarriesAMarkerForAServerSideValue()
    {
        foreach (var asset in EmbeddedPageAssets())
        {
            var found = Regex.Matches(Text(asset.Value), SubstitutionMarker)
                .Select(match => match.Value)
                .ToArray();

            Assert.True(
                found.Length == 0,
                asset.Key + " carries " + found.Length + " substitution marker(s): " + string.Join(", ", found) + ". A page is served to an unauthenticated caller, so every value it shows is fetched after it loads and none is filled in for it.");
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
    /// <param name="name">A resource name or a file path.</param>
    /// <returns>True where the name is a page asset.</returns>
    private static bool IsPageAsset(string name)
    {
        return name.EndsWith(".html", StringComparison.Ordinal);
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
