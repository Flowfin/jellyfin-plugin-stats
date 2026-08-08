// What an uninstall leaves behind, checked at both ends: the hook the server
// calls, and the function underneath it that does the deleting.
//
// The function is driven over paths the test made up, including the assembly
// path. Nothing here points at the directory the suite is running out of: the
// case where the data folder is the folder the assembly was loaded from is the
// case where a wrong answer deletes the caller, and a test proving it with the
// real one would be a test that deletes the suite when it fails.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class UninstallRemovesTheDataTests : IDisposable
{
    private readonly string _root;

    public UninstallRemovesTheDataTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TheUninstallHookRemovesTheDataFolderAndTheConfigurationFile()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = new Plugin(new FakeApplicationPaths(_root), RefusingXmlSerializer.Instance, logger);

        // Read off the plugin rather than rebuilt here, so this is the folder
        // and the file the running server would use and not a second opinion
        // about where they are.
        Populate(plugin.DataFolderPath);
        Write(plugin.ConfigurationFilePath, "<PluginConfiguration />");

        plugin.OnUninstalling();

        Assert.False(Directory.Exists(plugin.DataFolderPath), plugin.DataFolderPath + " is still there.");
        Assert.False(File.Exists(plugin.ConfigurationFilePath), plugin.ConfigurationFilePath + " is still there.");
        Assert.Empty(logger.Lines);
    }

    [Fact]
    public void TheDataFolderGoesWithEverythingUnderIt()
    {
        var logger = new RecordingLogger<Plugin>();
        var dataFolder = Path.Combine(_root, "plugins", "Jellyfin.Plugin.Stats");
        Populate(dataFolder);

        PluginDataRemoval.Remove(dataFolder, Path.Combine(_root, "configurations", "Jellyfin.Plugin.Stats.xml"), AnAssemblyElsewhere(), logger);

        Assert.False(Directory.Exists(dataFolder));
        Assert.Empty(logger.Lines);
    }

    [Fact]
    public void APluginThatWroteNothingUninstallsQuietly()
    {
        var logger = new RecordingLogger<Plugin>();

        PluginDataRemoval.Remove(
            Path.Combine(_root, "plugins", "Jellyfin.Plugin.Stats"),
            Path.Combine(_root, "configurations", "Jellyfin.Plugin.Stats.xml"),
            AnAssemblyElsewhere(),
            logger);

        Assert.Empty(logger.Lines);
    }

    /// <summary>
    /// A hand installation puts the assembly in a folder named after it, and
    /// the data folder is then that same folder. Deleting it here would take
    /// the running assembly and the manifest beside it, in the middle of the
    /// server's own uninstall of the same folder.
    /// </summary>
    [Fact]
    public void TheDataFolderIsLeftAloneWhenItIsTheFolderTheAssemblyWasLoadedFrom()
    {
        var logger = new RecordingLogger<Plugin>();
        var dataFolder = Path.Combine(_root, "plugins", "Jellyfin.Plugin.Stats");
        Populate(dataFolder);

        PluginDataRemoval.Remove(
            dataFolder,
            Path.Combine(_root, "configurations", "Jellyfin.Plugin.Stats.xml"),
            Path.Combine(dataFolder, "Jellyfin.Plugin.Stats.dll"),
            logger);

        Assert.True(Directory.Exists(dataFolder));
        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Contains(dataFolder, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whatever could not be deleted is named on the log with its path, which
    /// is the only thing that lets an administrator finish the removal by hand.
    /// Both ways a deletion fails are driven here, because they arrive as
    /// different exceptions and a handler for one is not a handler for the
    /// other.
    /// </summary>
    [Fact]
    public void AFolderThatCouldNotBeRemovedIsNamedOnTheLog()
    {
        var logger = new RecordingLogger<Plugin>();

        // A file where the folder is expected. The removal has to say so
        // rather than pass over it as if the folder had already gone.
        var dataFolder = Path.Combine(_root, "plugins", "Jellyfin.Plugin.Stats");
        Write(dataFolder, "not a folder");

        PluginDataRemoval.Remove(dataFolder, Path.Combine(_root, "configurations", "Jellyfin.Plugin.Stats.xml"), AnAssemblyElsewhere(), logger);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(dataFolder, line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AConfigurationFileThatCouldNotBeRemovedIsNamedOnTheLog()
    {
        var logger = new RecordingLogger<Plugin>();

        // A directory where the file is expected.
        var configurationFile = Path.Combine(_root, "configurations", "Jellyfin.Plugin.Stats.xml");
        Directory.CreateDirectory(configurationFile);

        PluginDataRemoval.Remove(Path.Combine(_root, "plugins", "Jellyfin.Plugin.Stats"), configurationFile, AnAssemblyElsewhere(), logger);

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(configurationFile, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page tells an administrator where the data is while the plugin is
    /// still installed, which is the only route to it that does not depend on
    /// the uninstall hook having run. Both names come from the assembly name,
    /// so a rename moves the real paths and leaves the page pointing at
    /// somewhere that no longer exists. This reads the page out of the built
    /// assembly, which is the copy a server loads.
    /// </summary>
    [Fact]
    public void TheConfigurationPageNamesThePathsThePluginActuallyUses()
    {
        var plugin = new Plugin(new FakeApplicationPaths(_root), RefusingXmlSerializer.Instance, new RecordingLogger<Plugin>());

        using var page = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Stats.Configuration.configPage.html");
        Assert.NotNull(page);
        using var reader = new StreamReader(page!);
        var markup = reader.ReadToEnd();

        Assert.Contains(Path.GetFileName(plugin.DataFolderPath), markup, StringComparison.Ordinal);
        Assert.Contains(Path.GetFileName(plugin.ConfigurationFilePath), markup, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// An assembly path that is not inside the data folder, which is the
    /// ordinary installation: the server unpacks into a folder named for the
    /// plugin and its version, and the data folder is named for the assembly.
    /// </summary>
    /// <returns>The path of an assembly file that no test creates.</returns>
    private string AnAssemblyElsewhere()
        => Path.Combine(_root, "plugins", "Playback Statistics_1.0.0.0", "Jellyfin.Plugin.Stats.dll");

    private static void Populate(string folder)
    {
        Write(Path.Combine(folder, "stats.db"), "rows");
        Write(Path.Combine(folder, "backup", "stats.db.1"), "older rows");
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// The plugin base class takes a serializer and this suite never asks it to
    /// read or write configuration. It refuses rather than returning nothing,
    /// so a test that starts depending on stored configuration fails here
    /// instead of quietly reading a default.
    /// </summary>
    private sealed class RefusingXmlSerializer : IXmlSerializer
    {
        public static RefusingXmlSerializer Instance { get; } = new();

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();

        public void SerializeToFile(object obj, string file) => throw new NotSupportedException();

        public object DeserializeFromFile(Type type, string file) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();
    }
}
