// What this plugin leaves on disk, checked by watching a whole cycle rather
// than by reading the code that writes.
//
// The server's data directory is a temporary one the test owns, and every file
// under it is listed before and after. That is the strongest form the claim can
// take from inside a process: a file written anywhere else on the machine is
// not something a test can see the absence of, and this is the tree an
// administrator backs up and an uninstall clears.
//
// The layout the third condition of issue #73 asks for is docs/plugin-data.md.
// The names below are the same ones, and this file is what keeps them true.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ServerPlayMethod = MediaBrowser.Model.Session.PlayMethod;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class DataFolderLayoutTests : IDisposable
{
    private readonly string _root;

    public DataFolderLayoutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task AWholeCycleWritesInsideTheDataFolderAndNowhereElse()
    {
        var plugin = APlugin();
        var before = EverythingUnder(_root);

        await AWholePlay(plugin.DataFolderPath);

        var written = EverythingUnder(_root).Except(before, StringComparer.Ordinal).ToList();

        // The two directories are the server's own plugin layout, read off the
        // plugin rather than spelled here, and the file is the store. Nothing
        // else is permitted to appear anywhere under the server's data
        // directory.
        var allowed = new[]
        {
            Path.GetDirectoryName(plugin.DataFolderPath)!,
            plugin.DataFolderPath,
            Path.Combine(plugin.DataFolderPath, SqlitePlayStore.FileName)
        };

        // Both directions. Nothing appeared outside the two places the layout
        // names, and nothing appeared inside the data folder that the layout
        // does not name either - which is the half that catches a temporary
        // file left behind, and the harder one to write, because it has to be
        // told what is allowed.
        Assert.Equal(allowed, written);
    }

    [Fact]
    public async Task TheStoreIsTheOnlyThingLeftBehindWhenItIsClosed()
    {
        var plugin = APlugin();

        await AWholePlay(plugin.DataFolderPath);

        // Named rather than counted. A rollback journal or a write-ahead pair
        // would be a second and a third file here, and the layout says there is
        // one; this is where that stops being a claim.
        Assert.Equal(
            [SqlitePlayStore.FileName],
            Directory.GetFiles(plugin.DataFolderPath).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(Directory.GetDirectories(plugin.DataFolderPath));
    }

    [Fact]
    public async Task AnExportThatFailsPartWayThroughLeavesNoFileAtAll()
    {
        var plugin = APlugin();
        await AWholePlay(plugin.DataFolderPath);

        var before = EverythingUnder(_root);

        using var store = new SqlitePlayStore(plugin.DataFolderPath);
        var destination = new FailsAfterTheFirstLine();

        Assert.Throws<IOException>(() => PlayArchive.Export(store.AllPlays(), destination));

        // The export writes to a destination the caller opened and never opens
        // one of its own, so a failure part way through has no temporary file
        // to leave. That is a property of the shape rather than of the tidying
        // up, and this is the run that says so.
        Assert.Equal(before, EverythingUnder(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A plugin whose paths are all under this test's own directory.
    /// </summary>
    private Plugin APlugin()
        => new(new FakeApplicationPaths(_root), RefusingXmlSerializer.Instance, NullLogger<Plugin>.Instance);

    /// <summary>
    /// Runs one play through the path a server drives, over a real store in the
    /// folder given, and waits until the write path has finished with it.
    /// </summary>
    private static async Task AWholePlay(string dataFolderPath)
    {
        using var writer = new QueuedPlayWriter(
            () => new SqlitePlayStore(dataFolderPath),
            QueuedPlayWriter.DefaultBound,
            NullLogger<QueuedPlayWriter>.Instance);

        var gate = new CaptureGate(writer, () => new PluginConfiguration());
        var tracker = new PlayTracker(gate, NullLogger<PlayTracker>.Instance);
        var sessions = new FakeSessionManager();
        var listener = new PlaybackEventListener(sessions, tracker, NullLogger<PlaybackEventListener>.Instance);

        await listener.StartAsync(CancellationToken.None);

        var session = new PlaySessionBuilder(sessions)
            .ForUser(FakeUserManager.NewUser("viewer"))
            .Playing(PlaySessionBuilder.Video("An Item", TimeSpan.FromMinutes(90)))
            .From("Jellyfin Web", "A browser")
            .Via(ServerPlayMethod.DirectPlay)
            .Build();

        sessions.RaisePlaybackStart(session);
        sessions.RaisePlaybackProgress(session, TimeSpan.FromMinutes(5));
        sessions.RaisePlaybackStopped(session, TimeSpan.FromMinutes(10));

        await listener.StopAsync(CancellationToken.None);

        // The writer holds the store open for its own lifetime, so this is also
        // what closes the file. A listing taken before it would be a listing of
        // a store mid-write, which is a different question.
        writer.Dispose();
    }

    private static List<string> EverythingUnder(string root)
        => Directory
            .GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A destination that takes one line and then fails, which is what a full
    /// disk or a lost network share looks like part way through an export.
    /// </summary>
    private sealed class FailsAfterTheFirstLine : TextWriter
    {
        private int _lines;

        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            _lines++;

            if (_lines > 1)
            {
                throw new IOException("the destination went away");
            }
        }
    }

    private sealed class RefusingXmlSerializer : IXmlSerializer
    {
        public static RefusingXmlSerializer Instance { get; } = new();

        public object DeserializeFromFile(Type type, string file) => throw new NotSupportedException();

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();

        public void SerializeToFile(object obj, string file) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();
    }
}
