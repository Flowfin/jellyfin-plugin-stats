// What happens to a settings file written by an older build of this plugin, and
// to one written by a newer build.
//
// The shapes are written out as text rather than built with the current model.
// A file produced by today's type is a file in today's shape, so a round trip
// through it would prove that this build can read itself and nothing about the
// build that wrote the file. The bytes below are the bytes those builds wrote.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class ConfigurationMigrationTests : IDisposable
{
    /// <summary>
    /// The settings file as the upstream plugin template wrote it, which is
    /// shape version zero and the only shape this plugin has had before the
    /// stamp existed. Taken from the file the template shipped rather than
    /// written from memory.
    /// </summary>
    private const string TemplateShape = """
        <?xml version="1.0" encoding="utf-8"?>
        <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
          <TrueFalseSetting>true</TrueFalseSetting>
          <AnInteger>2</AnInteger>
          <AString>string</AString>
          <Options>AnotherOption</Options>
        </PluginConfiguration>
        """;

    private readonly string _root;

    public ConfigurationMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// Every shape a released build of this plugin has written, oldest first,
    /// and what the current build reads out of it. A shape is added here when a
    /// step is added to the chain, and the row is what says the step did what it
    /// was written to do rather than merely running.
    /// </summary>
    /// <returns>One case per earlier shape.</returns>
    public static TheoryData<string, string> EarlierShapes()
    {
        return new TheoryData<string, string> { { "the template's example settings", TemplateShape } };
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A file from an earlier shape loads, and every setting this plugin has is
    /// the value that plugin would use. This is the whole point of the exercise:
    /// an upgrade that read the file and quietly produced something else is the
    /// failure, and it produces no error of its own.
    /// </summary>
    /// <param name="shape">What the shape is, for the test name.</param>
    /// <param name="stored">The file as that build wrote it.</param>
    [Theory]
    [MemberData(nameof(EarlierShapes))]
    public void AnEarlierShapeLoadsIntoTheSettingsThisBuildReads(string shape, string stored)
    {
        _ = shape;

        var plugin = PluginOver(stored);
        var configuration = plugin.Configuration;

        Assert.Equal(ConfigurationMigrations.Current, configuration.ConfigurationVersion);
        Assert.Equal(ConfigurationLimits.DefaultCaptureEnabled, configuration.CaptureEnabled);
        Assert.Equal(ConfigurationLimits.DefaultPlayRowRetentionDays, configuration.PlayRowRetentionDays);
        Assert.Equal(ConfigurationLimits.DefaultDailyAggregateRetentionDays, configuration.DailyAggregateRetentionDays);
        Assert.Equal(ConfigurationLimits.DefaultMaximumRangeDays, configuration.MaximumRangeDays);
        Assert.Equal(ConfigurationLimits.DefaultMaximumRowsPerResponse, configuration.MaximumRowsPerResponse);
        Assert.Equal(ConfigurationLimits.DefaultRollupTimeZone, configuration.RollupTimeZone);
        Assert.Empty(configuration.ExcludedUserIds);
        Assert.Empty(configuration.ExcludedItemTypes);

        // Nothing was refused. A migration that left an element the model does
        // not accept would show up here rather than as a wrong value later.
        Assert.Empty(configuration.RejectedFields);
    }

    /// <summary>
    /// The file on disk is moved rather than only the object in memory. Left
    /// alone on disk, the next start would migrate it again, and a step that is
    /// not safe to repeat would then be run twice on the same file.
    /// </summary>
    /// <param name="shape">What the shape is, for the test name.</param>
    /// <param name="stored">The file as that build wrote it.</param>
    [Theory]
    [MemberData(nameof(EarlierShapes))]
    public void AnEarlierShapeIsMovedOnDiskAndNotOnlyInMemory(string shape, string stored)
    {
        _ = shape;

        var plugin = PluginOver(stored);
        _ = plugin.Configuration;

        var written = XElement.Load(ConfigurationFile);

        Assert.Equal(ConfigurationMigrations.Current, ConfigurationMigrator.VersionOf(written));
        Assert.Empty(written.Elements("TrueFalseSetting"));
        Assert.Empty(written.Elements("AnInteger"));
        Assert.Empty(written.Elements("AString"));
        Assert.Empty(written.Elements("Options"));
    }

    /// <summary>
    /// The move is reported once, naming the version found and the version moved
    /// to, so an administrator reading the log after an upgrade can tell whether
    /// their settings file was touched.
    /// </summary>
    [Fact]
    public void TheMoveIsReportedWithBothVersions()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = PluginOver(TemplateShape, logger);

        _ = plugin.Configuration;

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains("shape version 0", line.Message, StringComparison.Ordinal);
        Assert.Contains("version " + ConfigurationMigrations.Current, line.Message, StringComparison.Ordinal);
        Assert.Contains(ConfigurationMigrations.Describe(0), line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file already in the current shape is not rewritten and nothing is said
    /// about it. A line on every start would be noise, and noise on a log is
    /// read as an event.
    /// </summary>
    [Fact]
    public void AFileAlreadyInTheCurrentShapeIsLeftAloneAndSaidNothingAbout()
    {
        var logger = new RecordingLogger<Plugin>();
        var stored = CurrentShape(retentionDays: 30);
        var plugin = PluginOver(stored, logger);

        _ = plugin.Configuration;

        Assert.Equal(stored, File.ReadAllText(ConfigurationFile));
        Assert.Empty(logger.Lines);
    }

    /// <summary>
    /// A server whose log is not turned up to information writes no line and
    /// does no work building one. The migration itself still happens: the file
    /// is the thing being repaired, and a log level is not a reason to leave a
    /// settings file in the wrong shape.
    /// </summary>
    [Fact]
    public void TheFileIsStillMovedWhereTheLevelIsNotEnabled()
    {
        var plugin = PluginOver(TemplateShape, QuietLogger.Instance);

        _ = plugin.Configuration;

        Assert.Equal(ConfigurationMigrations.Current, ConfigurationMigrator.VersionOf(XElement.Load(ConfigurationFile)));
    }

    /// <summary>
    /// A file stamped later than this build reaches is left exactly as it is,
    /// and both numbers are on the log. It may hold settings this build has no
    /// property for, and the server's writer drops what it does not recognise,
    /// so writing over it is the one thing that cannot be undone.
    /// </summary>
    [Fact]
    public void AFileFromALaterVersionIsLeftAsItIsAndBothVersionsAreSaid()
    {
        var logger = new RecordingLogger<Plugin>();
        var stored = CurrentShape(retentionDays: 30, version: ConfigurationMigrations.Current + 2)
            .Replace("</PluginConfiguration>", "  <SomethingLaterInvented>7</SomethingLaterInvented>\n</PluginConfiguration>", StringComparison.Ordinal);
        var plugin = PluginOver(stored, logger);

        _ = plugin.Configuration;

        Assert.Equal(stored, File.ReadAllText(ConfigurationFile));

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains("shape version " + (ConfigurationMigrations.Current + 2), line.Message, StringComparison.Ordinal);
        Assert.Contains("version " + ConfigurationMigrations.Current, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every way the server has of writing this plugin's settings is refused
    /// while the stored file is from a later version. There is one guard and it
    /// is on the method the other two come through, which is what makes this a
    /// statement about the plugin rather than about three copies of a check.
    /// </summary>
    [Fact]
    public void EveryWayOfSavingIsRefusedWhileTheStoredFileIsNewer()
    {
        var plugin = PluginOver(CurrentShape(retentionDays: 30, version: ConfigurationMigrations.Current + 1));
        var configuration = new PluginConfiguration();

        foreach (var save in new (string Name, Action Act)[]
        {
            ("SaveConfiguration(configuration)", () => plugin.SaveConfiguration(configuration)),
            ("SaveConfiguration()", plugin.SaveConfiguration),
            ("UpdateConfiguration(configuration)", () => plugin.UpdateConfiguration(configuration))
        })
        {
            var refused = Assert.Throws<ConfigurationIsNewerThanThePluginException>(save.Act);

            Assert.Equal(ConfigurationMigrations.Current + 1, refused.StoredVersion);
            Assert.Equal(ConfigurationMigrations.Current, refused.PluginVersion);
            Assert.Contains(save.Name[..4], save.Name, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The guard refuses and nothing else does. With the stored file at this
    /// build's own version the save goes through to disk, carrying the stamp
    /// with it. Without this the test above would pass just as well against a
    /// plugin that never saved anything at all.
    /// </summary>
    [Fact]
    public void ASaveGoesThroughToDiskWhereTheStoredFileIsNotNewer()
    {
        var plugin = PluginOver(CurrentShape(retentionDays: 30), serializer: WritingXmlSerializer.Instance);

        plugin.SaveConfiguration(new PluginConfiguration { PlayRowRetentionDays = 45 });

        var written = XElement.Load(ConfigurationFile);

        Assert.Equal(ConfigurationMigrations.Current, ConfigurationMigrator.VersionOf(written));
        Assert.Equal("45", written.Element("PlayRowRetentionDays")?.Value);
    }

    /// <summary>
    /// A settings file that is not readable as XML stops the plugin from
    /// migrating it and stops nothing else. The server writes a fresh default
    /// over a file it cannot parse, so this is the case where that has not
    /// happened yet, and a plugin that threw here would take the start-up with
    /// it over a file the server was about to replace.
    /// </summary>
    [Fact]
    public void AFileThatIsNotXmlIsReportedAndDoesNotThrow()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = PluginOver("this is not xml", logger);

        _ = plugin.ConfigurationFileName;

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(ConfigurationFile, line.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A directory sitting where the settings file belongs is the read-only and
    /// locked cases in the shape a test can make without changing the machine it
    /// runs on. It is reported and it does not throw.
    /// </summary>
    [Fact]
    public void AFileThatCannotBeReadIsReportedAndDoesNotThrow()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = new Plugin(new FakeApplicationPaths(_root), RefusingXmlSerializer.Instance, logger);

        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationFile)!);
        Directory.CreateDirectory(ConfigurationFile);

        _ = plugin.ConfigurationFileName;

        var line = Assert.Single(logger.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
    }

    /// <summary>
    /// Asking where the settings file is, before there is one, does not spend
    /// the single attempt the plugin makes at migrating it. The uninstall hook
    /// reads that path on a server where nothing was ever saved, and a flag set
    /// there would leave the migration never running on the load that follows.
    /// </summary>
    [Fact]
    public void ReadingThePathBeforeTheFileExistsDoesNotSpendTheAttempt()
    {
        var plugin = new Plugin(new FakeApplicationPaths(_root), ReadingXmlSerializer.Instance, new RecordingLogger<Plugin>());

        _ = plugin.ConfigurationFilePath;

        Write(TemplateShape);

        Assert.Equal(ConfigurationMigrations.Current, plugin.Configuration.ConfigurationVersion);
    }

    /// <summary>
    /// The file is migrated once. The path is read on every load and on every
    /// save, and a migration that ran each time would rewrite the file under a
    /// server that had already read it.
    /// </summary>
    [Fact]
    public void TheFileIsMovedOnceHoweverOftenThePathIsRead()
    {
        var logger = new RecordingLogger<Plugin>();
        var plugin = PluginOver(TemplateShape, logger);

        _ = plugin.ConfigurationFileName;
        _ = plugin.ConfigurationFileName;
        _ = plugin.ConfigurationFileName;

        Assert.Single(logger.Lines);
    }

    /// <summary>
    /// An upgrade that skips a release does what the releases it skipped would
    /// have done, in the order they would have done it. This is the case least
    /// likely to be tried by hand and most likely to exist on somebody's server,
    /// because a plugin is usually upgraded from whatever was installed a year
    /// ago.
    /// </summary>
    /// <remarks>
    /// The chain here is written for this test. The real one has a single step
    /// today, so a property about composing steps could not be shown over it at
    /// all, and a test that waited for a second real step would be a test that
    /// arrives after the mistake it exists to catch.
    /// </remarks>
    [Fact]
    public void AnUpgradeAcrossVersionsIsTheUpgradesInSequence()
    {
        var chain = ThreeSteps();

        var atOnce = XElement.Parse("<PluginConfiguration><Kept>keep me</Kept></PluginConfiguration>");
        ConfigurationMigrator.Migrate(atOnce, chain);

        var inSequence = XElement.Parse("<PluginConfiguration><Kept>keep me</Kept></PluginConfiguration>");
        for (var step = 1; step <= chain.Count; step++)
        {
            ConfigurationMigrator.Migrate(inSequence, chain.Take(step).ToList());
        }

        Assert.Equal(atOnce.ToString(), inSequence.ToString());
        Assert.Equal(chain.Count, ConfigurationMigrator.VersionOf(atOnce));
    }

    /// <summary>
    /// A step is run once per version it stands between, and no step is skipped.
    /// The test above compares two results and would be satisfied by a chain
    /// that ran nothing at all in both.
    /// </summary>
    [Fact]
    public void EveryStepBetweenTheStoredVersionAndThisOneRunsInOrder()
    {
        var ran = new List<string>();
        var chain = new List<ConfigurationMigration>
        {
            new("first", root => ran.Add("first:" + root.Name.LocalName)),
            new("second", root => ran.Add("second:" + root.Name.LocalName)),
            new("third", root => ran.Add("third:" + root.Name.LocalName))
        };

        var from = ConfigurationMigrator.Migrate(AtVersion(1), chain);

        Assert.Equal(1, from);
        Assert.Equal(["second:PluginConfiguration", "third:PluginConfiguration"], ran);
    }

    /// <summary>
    /// A file already at the end of the chain is not touched and says so by
    /// answering with nothing rather than with a version.
    /// </summary>
    [Fact]
    public void AFileAtTheEndOfTheChainIsNotTouched()
    {
        var chain = ThreeSteps();
        var root = AtVersion(chain.Count);

        Assert.Null(ConfigurationMigrator.Migrate(root, chain));
    }

    /// <summary>
    /// What a stored file has to carry before this plugin believes a version
    /// number in it. Anything else reads as version zero, which runs the chain
    /// over a file that may not have needed it; the other direction would skip a
    /// step over a file that did.
    /// </summary>
    /// <param name="stamp">What the version element holds, or null for no element at all.</param>
    /// <param name="expected">The version this plugin reads.</param>
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 0)]
    [InlineData("-3", 0)]
    [InlineData("0", 0)]
    [InlineData("2", 2)]
    [InlineData("99", 99)]
    public void OnlyANumberThisPluginCouldHaveWrittenReadsAsAVersion(string? stamp, int expected)
    {
        var root = XElement.Parse("<PluginConfiguration />");

        if (stamp is not null)
        {
            root.Add(new XElement(ConfigurationMigrator.VersionElementName, stamp));
        }

        Assert.Equal(expected, ConfigurationMigrator.VersionOf(root));
    }

    /// <summary>
    /// A file carrying a version this plugin does not believe is stamped rather
    /// than given a second element. Two version elements in one file is a file
    /// whose version depends on which one is read first.
    /// </summary>
    [Fact]
    public void AVersionThatCannotBeReadIsWrittenOverRatherThanAddedTo()
    {
        var root = XElement.Parse(
            "<PluginConfiguration><" + ConfigurationMigrator.VersionElementName + ">rubbish</"
            + ConfigurationMigrator.VersionElementName + "></PluginConfiguration>");

        ConfigurationMigrator.Migrate(root, ThreeSteps());

        Assert.Single(root.Elements(ConfigurationMigrator.VersionElementName));
        Assert.Equal(3, ConfigurationMigrator.VersionOf(root));
    }

    /// <summary>
    /// A settings file that is not there is a fresh installation. The server
    /// writes one the first time anything is saved, and the model stamps it at
    /// the current version on its own.
    /// </summary>
    [Fact]
    public void AFileThatIsNotThereIsNotAFailure()
    {
        Assert.Null(ConfigurationMigrator.MigrateFile(ConfigurationFile, ConfigurationMigrations.All));
        Assert.Equal(ConfigurationMigrations.Current, new PluginConfiguration().ConfigurationVersion);
    }

    /// <summary>
    /// The write guard's question is whether saving would destroy something
    /// newer, and a file nobody can read is not evidence that it would. Answering
    /// zero there would be answering a different question, and answering with a
    /// refusal would leave a settings page that cannot save and no way to repair
    /// it from the page.
    /// </summary>
    [Fact]
    public void AFileThatCannotBeReadAnswersWithWhatTheCallerAsksedFor()
    {
        Write("not xml either");

        Assert.Equal(41, ConfigurationMigrator.VersionOfFile(ConfigurationFile, whereUnreadable: 41));
    }

    /// <summary>
    /// A step says what it changed, and the sentence names every step that ran
    /// rather than only the two version numbers. Two numbers do not tell an
    /// administrator whether the setting they are looking at was one of the ones
    /// that moved.
    /// </summary>
    [Fact]
    public void WhatAnUpgradeDidIsNamedStepByStep()
    {
        Assert.Equal("second; third", DescribeOver(ThreeSteps(), from: 1));
        Assert.Equal(string.Empty, DescribeOver(ThreeSteps(), from: 3));
        Assert.NotEmpty(ConfigurationMigrations.Describe(0));
    }

    /// <summary>
    /// A step with nothing to say about itself is refused where it is built. The
    /// description is what an upgrade reports, and a step that reported nothing
    /// would make the log line say that something changed without saying what.
    /// </summary>
    [Fact]
    public void AStepHasToSayWhatItChanged()
    {
        Assert.Throws<ArgumentException>(() => new ConfigurationMigration("  ", _ => { }));
        Assert.Throws<ArgumentNullException>(() => new ConfigurationMigration("something", null!));
    }

    /// <summary>
    /// Where the settings file sits for this test's server.
    /// </summary>
    private string ConfigurationFile =>
        Path.Combine(_root, "plugins", "configurations", Path.ChangeExtension(Path.GetFileName(typeof(Plugin).Assembly.Location), ".xml"));

    /// <summary>
    /// Three steps that record nothing and change one element each, so a result
    /// tells the steps apart.
    /// </summary>
    /// <returns>The chain.</returns>
    private static List<ConfigurationMigration> ThreeSteps()
    {
        return
        [
            new ConfigurationMigration("first", root => root.Add(new XElement("First", "1"))),
            new ConfigurationMigration("second", root => root.Add(new XElement("Second", "2"))),
            new ConfigurationMigration("third", root => root.Element("First")?.Remove())
        ];
    }

    /// <summary>
    /// Reads what a chain would say about a move, without going through the real
    /// one.
    /// </summary>
    /// <param name="chain">The steps.</param>
    /// <param name="from">The version moved away from.</param>
    /// <returns>The sentence.</returns>
    private static string DescribeOver(List<ConfigurationMigration> chain, int from)
    {
        return string.Join("; ", chain.Skip(from).Select(step => step.Describes));
    }

    /// <summary>
    /// A stored file carrying only a version.
    /// </summary>
    /// <param name="version">The version to stamp.</param>
    /// <returns>The root element.</returns>
    private static XElement AtVersion(int version)
    {
        return XElement.Parse(
            "<PluginConfiguration><" + ConfigurationMigrator.VersionElementName + ">" + version + "</"
            + ConfigurationMigrator.VersionElementName + "></PluginConfiguration>");
    }

    /// <summary>
    /// A settings file in the shape this build writes.
    /// </summary>
    /// <param name="retentionDays">A setting with a value that is not the default, so a test can tell a kept file from a rewritten one.</param>
    /// <param name="version">The version to stamp, defaulting to this build's.</param>
    /// <returns>The file's text.</returns>
    private static string CurrentShape(int retentionDays, int? version = null)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<PluginConfiguration>\n  <"
            + ConfigurationMigrator.VersionElementName + ">" + (version ?? ConfigurationMigrations.Current) + "</"
            + ConfigurationMigrator.VersionElementName + ">\n  <PlayRowRetentionDays>" + retentionDays
            + "</PlayRowRetentionDays>\n</PluginConfiguration>";
    }

    /// <summary>
    /// A plugin whose settings file already holds the given text.
    /// </summary>
    /// <param name="stored">What is on disk before the plugin looks.</param>
    /// <param name="logger">The logger, or a recording one.</param>
    /// <param name="serializer">The server's serializer, or one that only reads.</param>
    /// <returns>The plugin.</returns>
    private Plugin PluginOver(string stored, ILogger<Plugin>? logger = null, IXmlSerializer? serializer = null)
    {
        Write(stored);

        return new Plugin(
            new FakeApplicationPaths(_root),
            serializer ?? ReadingXmlSerializer.Instance,
            logger ?? new RecordingLogger<Plugin>());
    }

    /// <summary>
    /// Puts a settings file where this test's server keeps one.
    /// </summary>
    /// <param name="stored">The file's text.</param>
    private void Write(string stored)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationFile)!);
        File.WriteAllText(ConfigurationFile, stored);
    }

    /// <summary>
    /// The server's own serializer, over the same type the server would use it
    /// on. The migration exists because this drops every element the current
    /// type has no property for, so a stand-in that kept them would test the
    /// stand-in.
    /// </summary>
    private sealed class ReadingXmlSerializer : IXmlSerializer
    {
        public static ReadingXmlSerializer Instance { get; } = new();

        public object DeserializeFromFile(Type type, string file)
        {
            using var reader = XmlReader.Create(file);
            return new System.Xml.Serialization.XmlSerializer(type).Deserialize(reader)!;
        }

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();

        public void SerializeToFile(object obj, string file) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();
    }

    /// <summary>
    /// The server's own serializer again, this time on the way out, so a test
    /// can read back what a save actually put on disk.
    /// </summary>
    private sealed class WritingXmlSerializer : IXmlSerializer
    {
        public static WritingXmlSerializer Instance { get; } = new();

        public void SerializeToFile(object obj, string file)
        {
            using var writer = XmlWriter.Create(file);
            new System.Xml.Serialization.XmlSerializer(obj.GetType()).Serialize(writer, obj);
        }

        public object DeserializeFromFile(Type type, string file) => ReadingXmlSerializer.Instance.DeserializeFromFile(type, file);

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();
    }

    /// <summary>
    /// A serializer that refuses everything, for the tests that must not reach
    /// one.
    /// </summary>
    private sealed class RefusingXmlSerializer : IXmlSerializer
    {
        public static RefusingXmlSerializer Instance { get; } = new();

        public object DeserializeFromFile(Type type, string file) => throw new NotSupportedException();

        public object DeserializeFromStream(Type type, Stream stream) => throw new NotSupportedException();

        public object DeserializeFromBytes(Type type, byte[] buffer) => throw new NotSupportedException();

        public void SerializeToFile(object obj, string file) => throw new NotSupportedException();

        public void SerializeToStream(object obj, Stream stream) => throw new NotSupportedException();
    }

    /// <summary>
    /// A logger with information turned off, which is what a server that nobody
    /// has turned the log up on looks like.
    /// </summary>
    private sealed class QuietLogger : ILogger<Plugin>
    {
        public static QuietLogger Instance { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
