// A save this plugin refuses leaves the process running on what the stored file
// holds. The server assigns the incoming object before it asks the plugin to
// save it - MediaBrowser.Common/Plugins/BasePluginOfT.cs at v10.11.11, line 167
// and then line 169 - so a guard on the save alone left the file right and the
// running configuration wrong until the next restart. Issue #331.
//
// The cases drive the route the server's configuration endpoint takes,
// UpdateConfiguration, and then ask two questions of what is left running: is it
// the stored file, field by field, and does the sweep that reads it measure its
// cutoff from the stored window. The second is the question that costs rows.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class ARefusedSaveLeavesTheRunningConfigurationTests : IDisposable
{
    /// <summary>
    /// The moment the sweep runs at. Fixed, for the reason every sweep case
    /// fixes it: a boundary is a value the case chose and not the day the suite
    /// happened to run on.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What the stored file keeps play rows for. Not the default, so a running
    /// configuration that fell back to the default is a different number from
    /// the file rather than the same one for a different reason.
    /// </summary>
    private const int StoredWindow = 400;

    /// <summary>
    /// A window no setter takes. The object carrying it is refused whole, and on
    /// that object the field sits at its default of ninety days.
    /// </summary>
    private const int RefusedWindow = 99999;

    private readonly string _root;

    public ARefusedSaveLeavesTheRunningConfigurationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// The first condition on issue #331: the server's route, a refused value,
    /// and afterwards a running configuration that is the stored file field by
    /// field, with nothing reported as refused in it.
    /// </summary>
    /// <remarks>
    /// The configuration is read before the save and the same object is
    /// expected afterwards. A server has already handed the settings page its
    /// configuration by the time a save arrives, so what is in force is a
    /// loaded object and not a file nobody has read yet, and a case that let the
    /// plugin load the file for the first time after the refusal would pass
    /// while proving less.
    /// </remarks>
    [Fact]
    public void TheServersRouteWithARefusedValueLeavesTheRunningConfigurationAsTheStoredFile()
    {
        var plugin = APluginWhoseStoredFileKeepsRowsFor(StoredWindow);
        var running = plugin.Configuration;
        var sent = new PluginConfiguration { PlayRowRetentionDays = RefusedWindow };

        var refused = Assert.Throws<ConfigurationValueRefusedException>(() => plugin.UpdateConfiguration(sent));

        Assert.Equal([nameof(PluginConfiguration.PlayRowRetentionDays)], refused.Fields);
        Assert.Same(running, plugin.Configuration);

        var stored = TheStoredFileReadBack();
        var compared = StoredProperties().ToList();
        Assert.NotEmpty(compared);

        foreach (var property in compared)
        {
            Assert.True(
                SameValue(property.GetValue(stored), property.GetValue(plugin.Configuration)),
                property.Name + " differs between the stored file and the running configuration.");
        }

        Assert.Equal(StoredWindow, plugin.Configuration.PlayRowRetentionDays);
        Assert.Empty(plugin.Configuration.RejectedFields);
    }

    /// <summary>
    /// The half of the first condition that costs rows. A row older than the
    /// default window and younger than the stored one is taken by a sweep that
    /// reads the refused object and kept by a sweep that reads the file, and the
    /// sweep is wired the way the server wires it: off the plugin instance, at
    /// the run.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ASweepAfterARefusedSaveMeasuresItsCutoffFromTheStoredWindow()
    {
        var plugin = APluginWhoseStoredFileKeepsRowsFor(StoredWindow);
        _ = plugin.Configuration;

        var between = Now.UtcDateTime.AddDays(-200);

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(between));
        }

        Assert.Throws<ConfigurationValueRefusedException>(
            () => plugin.UpdateConfiguration(new PluginConfiguration { PlayRowRetentionDays = RefusedWindow }));

        var task = new RetentionSweepTask(
            new RetentionSweep(() => new SqlitePlayStore(_root), RetentionSweep.DefaultBite),
            new FixedClock(Now),
            () => plugin.Configuration);

        await task.ExecuteAsync(new IgnoredProgress(), CancellationToken.None);

        using var after = new SqlitePlayStore(_root);
        Assert.Equal([between], after.AllPlays().Select(play => play.StartedUtc));
    }

    /// <summary>
    /// The guard refuses a bad value and nothing else. A save the setters accept
    /// still replaces the running configuration and still reaches the file,
    /// which is what separates a guard from a route that stopped saving.
    /// </summary>
    [Fact]
    public void ASaveTheGuardAcceptsStillReplacesTheRunningConfigurationAndWritesTheFile()
    {
        var plugin = APluginWhoseStoredFileKeepsRowsFor(StoredWindow);
        _ = plugin.Configuration;
        var sent = new PluginConfiguration { PlayRowRetentionDays = 45 };

        plugin.UpdateConfiguration(sent);

        Assert.Same(sent, plugin.Configuration);
        Assert.Equal("45", XElement.Load(ConfigurationFile).Element(nameof(PluginConfiguration.PlayRowRetentionDays))?.Value);
    }

    /// <summary>
    /// The refusal is said once. The guard on the server's route stops the save
    /// before the file route runs, so an operator reads one line naming the
    /// field and not the same line twice for one save.
    /// </summary>
    [Fact]
    public void ARefusedSaveOnTheServersRouteIsSaidOnceOnTheLog()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = APluginWhoseStoredFileKeepsRowsFor(StoredWindow, logger);
        _ = plugin.Configuration;

        Assert.Throws<ConfigurationValueRefusedException>(
            () => plugin.UpdateConfiguration(new PluginConfiguration { PlayRowRetentionDays = RefusedWindow }));

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(nameof(PluginConfiguration.PlayRowRetentionDays), line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the settings file sits for this test's server.
    /// </summary>
    private string ConfigurationFile =>
        Path.Combine(_root, "plugins", "configurations", Path.ChangeExtension(Path.GetFileName(typeof(Plugin).Assembly.Location), ".xml"));

    /// <summary>
    /// The properties the stored file carries: everything on the model that can
    /// be written, which is what the server's serializer writes and reads.
    /// </summary>
    /// <returns>The properties, in declaration order.</returns>
    private static IEnumerable<PropertyInfo> StoredProperties()
    {
        return typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite);
    }

    /// <summary>
    /// Compares two property values, element by element where they are lists.
    /// </summary>
    /// <param name="stored">The value read out of the file.</param>
    /// <param name="running">The value on the running configuration.</param>
    /// <returns>True where the two say the same thing.</returns>
    private static bool SameValue(object? stored, object? running)
    {
        if (stored is string[] storedList && running is string[] runningList)
        {
            return storedList.SequenceEqual(runningList, StringComparer.Ordinal);
        }

        return Equals(stored, running);
    }

    /// <summary>
    /// A plugin whose settings file is in this build's shape and keeps play rows
    /// for the given number of days.
    /// </summary>
    /// <param name="retentionDays">What the file says the play rows are kept for.</param>
    /// <param name="logger">The logger, or a recording one.</param>
    /// <returns>The plugin.</returns>
    private Plugin APluginWhoseStoredFileKeepsRowsFor(int retentionDays, ILogger<Plugin>? logger = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationFile)!);
        File.WriteAllText(
            ConfigurationFile,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<PluginConfiguration>\n  <"
            + ConfigurationMigrator.VersionElementName + ">" + ConfigurationMigrations.Current + "</"
            + ConfigurationMigrator.VersionElementName + ">\n  <PlayRowRetentionDays>" + retentionDays
            + "</PlayRowRetentionDays>\n</PluginConfiguration>");

        return new Plugin(new FakeApplicationPaths(_root), ServersXmlSerializer.Instance, logger ?? new RecordingLogger<Plugin>());
    }

    /// <summary>
    /// The stored file, read the way the server reads it.
    /// </summary>
    /// <returns>The configuration the file holds.</returns>
    private PluginConfiguration TheStoredFileReadBack()
        => (PluginConfiguration)ServersXmlSerializer.Instance.DeserializeFromFile(typeof(PluginConfiguration), ConfigurationFile);

    private static PlayRecord APlayStartedAt(DateTime startedUtc)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = []
            }
        };
    }

    /// <summary>
    /// The server's own serializer, over the same type the server uses it on,
    /// in both directions. A stand-in that kept what this drops, or wrote what
    /// this omits, would test the stand-in.
    /// </summary>
    private sealed class ServersXmlSerializer : IXmlSerializer
    {
        public static ServersXmlSerializer Instance { get; } = new();

        public object DeserializeFromFile(Type type, string file)
        {
            using var reader = XmlReader.Create(file);
            return new System.Xml.Serialization.XmlSerializer(type).Deserialize(reader)!;
        }

        public void SerializeToFile(object obj, string file)
        {
            using var writer = XmlWriter.Create(file);
            new System.Xml.Serialization.XmlSerializer(obj.GetType()).Serialize(writer, obj);
        }

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();
    }

    private sealed class IgnoredProgress : IProgress<double>
    {
        public void Report(double value)
        {
        }
    }
}
