using System;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// Thrown when the store on disk was written by a later build than the one
/// running.
/// </summary>
/// <remarks>
/// This is the downgrade case: a server that ran a newer plugin and now runs an
/// older one. The rows are in a shape this build does not know, and there is no
/// step list going backwards, so the only two answers are to refuse or to guess.
/// It refuses, and it names both numbers so an administrator can tell which
/// build to put back rather than reading it off a stack trace.
/// <para>
/// A type of its own rather than a plain invalid operation, because the caller
/// that has to keep the server running through this has to be able to tell it
/// apart from a store that is merely broken. That caller is issue #31.
/// </para>
/// </remarks>
public sealed class StoreIsNewerThanThePluginException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoreIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="storeVersion">The version found in the store.</param>
    /// <param name="pluginVersion">The newest version this build knows.</param>
    public StoreIsNewerThanThePluginException(int storeVersion, int pluginVersion)
        : base(Describe(storeVersion, pluginVersion))
    {
        StoreVersion = storeVersion;
        PluginVersion = pluginVersion;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreIsNewerThanThePluginException"/> class.
    /// </summary>
    public StoreIsNewerThanThePluginException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public StoreIsNewerThanThePluginException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    public StoreIsNewerThanThePluginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets the schema version found in the store.
    /// </summary>
    public int StoreVersion { get; }

    /// <summary>
    /// Gets the newest schema version this build knows how to read.
    /// </summary>
    public int PluginVersion { get; }

    private static string Describe(int storeVersion, int pluginVersion)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "The store is at schema version {0} and this build of the plugin knows version {1}. It was written by a later build, there is no step list going backwards, and the rows are left untouched. Install the newer plugin again, or move the store aside.",
            storeVersion,
            pluginVersion);
    }
}
