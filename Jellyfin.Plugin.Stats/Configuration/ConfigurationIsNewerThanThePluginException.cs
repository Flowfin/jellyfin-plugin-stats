using System;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Thrown where the stored configuration was written by a later version of this
/// plugin than the one running.
/// </summary>
/// <remarks>
/// This is the downgrade case: a server that ran a newer plugin, then went back
/// to an older one. The stored file may hold settings this build has no property
/// for, and the serializer drops what it does not recognise, so writing over it
/// would silently delete them. Refusing is the milder failure. The settings page
/// stops saving and says why, and the operator can go forward again or remove
/// the file deliberately.
/// <para>
/// It carries both numbers rather than a sentence, so a caller can report them
/// without parsing the message back apart. It has the one constructor that can
/// supply them and none of the three an exception type usually carries: this is
/// thrown from one place, and a constructor nothing calls is a line the suite
/// cannot speak for.
/// </para>
/// </remarks>
public class ConfigurationIsNewerThanThePluginException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationIsNewerThanThePluginException"/> class.
    /// </summary>
    /// <param name="storedVersion">The version stamped on the file.</param>
    /// <param name="pluginVersion">The version this build writes.</param>
    public ConfigurationIsNewerThanThePluginException(int storedVersion, int pluginVersion)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "The stored configuration is at shape version {0} and this plugin writes version {1}. It was written by a later version of this plugin, so it is left as it is rather than written over.",
            storedVersion,
            pluginVersion))
    {
        StoredVersion = storedVersion;
        PluginVersion = pluginVersion;
    }

    /// <summary>
    /// Gets the shape version stamped on the stored file.
    /// </summary>
    public int StoredVersion { get; }

    /// <summary>
    /// Gets the shape version this build of the plugin writes.
    /// </summary>
    public int PluginVersion { get; }
}
