using System;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Thrown when an archive being imported was written under a later schema than
/// the build reading it.
/// </summary>
/// <remarks>
/// The same downgrade case as <see cref="StoreIsNewerThanThePluginException"/>
/// and a separate type because the two are different acts: that one is a server
/// that ran a newer plugin and now runs an older one, this one is a file
/// somebody carried here from a server that did. The rows in it are in a shape
/// this build does not know, and the two answers are to refuse or to guess at
/// what the fields it cannot see would have meant.
/// <para>
/// It refuses, and it names both numbers, because an administrator moving data
/// between servers needs to know which of the two builds to change rather than
/// that something did not work.
/// </para>
/// </remarks>
public sealed class ArchiveIsNewerThanThePluginException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="archiveVersion">The version the archive declares.</param>
    /// <param name="pluginVersion">The newest version this build knows.</param>
    public ArchiveIsNewerThanThePluginException(int archiveVersion, int pluginVersion)
        : base(Describe(archiveVersion, pluginVersion))
    {
        ArchiveVersion = archiveVersion;
        PluginVersion = pluginVersion;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveIsNewerThanThePluginException"/> class.
    /// </summary>
    public ArchiveIsNewerThanThePluginException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ArchiveIsNewerThanThePluginException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public ArchiveIsNewerThanThePluginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the schema version the archive declares.
    /// </summary>
    public int ArchiveVersion { get; }

    /// <summary>
    /// Gets the newest schema version this build knows how to read.
    /// </summary>
    public int PluginVersion { get; }

    private static string Describe(int archiveVersion, int pluginVersion)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "The archive was written at schema version {0} and this build of the plugin knows version {1}. Nothing was imported. Read it with the newer plugin, or export it again from a server running a build this one can follow.",
            archiveVersion,
            pluginVersion);
    }
}
