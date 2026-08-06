// The server's paths, rooted at a directory the test owns. Every member
// answers, because unlike the manager fakes this interface is all paths and a
// member that threw would only say which property the code under test happened
// to read on the day it was written.

using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// An <see cref="IApplicationPaths"/> under one temporary directory.
/// </summary>
public sealed class FakeApplicationPaths : IApplicationPaths
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FakeApplicationPaths"/> class.
    /// </summary>
    /// <param name="root">The directory every path below sits under. It is not created here; a test creates the parts of it that it needs.</param>
    public FakeApplicationPaths(string root)
    {
        ProgramDataPath = root;
    }

    /// <inheritdoc />
    public string ProgramDataPath { get; }

    /// <inheritdoc />
    public string WebPath => Path.Combine(ProgramDataPath, "web");

    /// <inheritdoc />
    public string ProgramSystemPath => Path.Combine(ProgramDataPath, "system");

    /// <inheritdoc />
    public string DataPath => Path.Combine(ProgramDataPath, "data");

    /// <inheritdoc />
    public string ImageCachePath => Path.Combine(ProgramDataPath, "cache", "images");

    /// <inheritdoc />
    public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");

    /// <inheritdoc />
    public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");

    /// <inheritdoc />
    public string LogDirectoryPath => Path.Combine(ProgramDataPath, "log");

    /// <inheritdoc />
    public string ConfigurationDirectoryPath => Path.Combine(ProgramDataPath, "config");

    /// <inheritdoc />
    public string SystemConfigurationFilePath => Path.Combine(ConfigurationDirectoryPath, "system.xml");

    /// <inheritdoc />
    public string CachePath => Path.Combine(ProgramDataPath, "cache");

    /// <inheritdoc />
    public string TempDirectory => Path.Combine(ProgramDataPath, "temp");

    /// <inheritdoc />
    public string VirtualDataPath => Path.Combine(ProgramDataPath, "virtual");

    /// <inheritdoc />
    public string TrickplayPath => Path.Combine(ProgramDataPath, "trickplay");

    /// <inheritdoc />
    public string BackupPath => Path.Combine(ProgramDataPath, "backup");

    /// <inheritdoc />
    /// <remarks>
    /// The two members that create directories refuse instead. A test decides
    /// which of these paths exists, and a fake that quietly made them all would
    /// take that decision away from it.
    /// </remarks>
    public void MakeSanityCheckOrThrow() => throw new NotSupportedException();

    /// <inheritdoc />
    public void CreateAndCheckMarker(string path, string markerName, bool recursive = false) => throw new NotSupportedException();
}
