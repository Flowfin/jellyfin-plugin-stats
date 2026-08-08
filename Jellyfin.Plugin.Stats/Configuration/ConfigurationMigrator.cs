using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Moves a stored configuration file from the shape it was written in to the
/// shape this build reads.
/// </summary>
/// <remarks>
/// The store has migrations and the configuration file did not, which is the
/// asymmetry this closes. A setting that is renamed reads as absent, and absent
/// is indistinguishable from never set, so the value goes back to its default
/// and nothing anywhere says it happened. That is the quietest way an upgrade
/// loses data.
/// <para>
/// It works on the XML rather than on the loaded object, and it has to. The
/// server hands the file to <see cref="System.Xml.Serialization.XmlSerializer"/>,
/// which drops every element the current type has no property for, so by the
/// time a <see cref="PluginConfiguration"/> exists the old value is already
/// gone. Running before the server's own load is the only place a rename can
/// still be carried across.
/// </para>
/// <para>
/// Every function here takes the chain as an argument rather than reaching for
/// <see cref="ConfigurationMigrations.All"/>. That is what lets the composition
/// property below be proved over a chain written for the test: the property is
/// about the migrator, not about the one step this plugin happens to have
/// today, and a proof that could only run against the real chain would stop
/// proving anything the moment the real chain had a single entry.
/// </para>
/// </remarks>
public static class ConfigurationMigrator
{
    /// <summary>
    /// The element the shape version is stored in.
    /// </summary>
    /// <remarks>
    /// The same name as the property on the model, taken from it rather than
    /// written out, because the serializer decides the element name from the
    /// property and a second spelling here would only be found by an upgrade
    /// that had already gone wrong.
    /// </remarks>
    public const string VersionElementName = nameof(PluginConfiguration.ConfigurationVersion);

    /// <summary>
    /// Reads the shape version a stored configuration carries.
    /// </summary>
    /// <remarks>
    /// Anything that is not a number this plugin could have written reads as
    /// version zero, and zero is the shape that predates the stamp. Erring that
    /// way runs the chain over a file that may not need it, which the steps are
    /// written to survive; erring the other way skips a step over a file that
    /// did need it, and the loss is silent.
    /// </remarks>
    /// <param name="root">The root element of the stored file.</param>
    /// <returns>The stored version, or zero where there is none this plugin recognises.</returns>
    public static int VersionOf(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var element = root.Element(VersionElementName);
        if (element is null)
        {
            return 0;
        }

        var readable = int.TryParse(element.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version);

        return readable && version > 0 ? version : 0;
    }

    /// <summary>
    /// Moves a stored configuration forward to the version the chain describes.
    /// </summary>
    /// <remarks>
    /// Applying the steps from where the file is to the end of the chain is what
    /// makes an upgrade across several versions the same thing as several
    /// upgrades in a row: there is one loop and it starts where the file starts.
    /// A migrator that instead ran "the newest step" would produce a different
    /// answer for a file that skipped a release, which is the file least likely
    /// to be tested and most likely to exist.
    /// </remarks>
    /// <param name="root">The root element of the stored file. It is changed in place.</param>
    /// <param name="chain">The steps, oldest first. Its length is the version it moves to.</param>
    /// <returns>The version the file was at, or null where nothing was done.</returns>
    /// <exception cref="ConfigurationIsNewerThanThePluginException">The file is stamped later than the chain reaches.</exception>
    public static int? Migrate(XElement root, IReadOnlyList<ConfigurationMigration> chain)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(chain);

        var stored = VersionOf(root);
        var current = chain.Count;

        if (stored > current)
        {
            throw new ConfigurationIsNewerThanThePluginException(stored, current);
        }

        if (stored == current)
        {
            return null;
        }

        for (var version = stored; version < current; version++)
        {
            chain[version].Apply(root);
        }

        Stamp(root, current);

        return stored;
    }

    /// <summary>
    /// Moves a stored configuration file forward, in place.
    /// </summary>
    /// <remarks>
    /// A file that is not there is a fresh installation and not a failure. The
    /// server writes one the first time settings are saved, and it writes it at
    /// the current version because the model's own default says so.
    /// </remarks>
    /// <param name="path">Where the stored configuration file is.</param>
    /// <param name="chain">The same steps <see cref="Migrate"/> takes.</param>
    /// <returns>The version the file was at, or null where nothing was written.</returns>
    /// <exception cref="ConfigurationIsNewerThanThePluginException">The file is stamped later than the chain reaches.</exception>
    /// <exception cref="System.Xml.XmlException">The file is not readable as XML.</exception>
    /// <exception cref="IOException">The file is locked, missing a directory above it, or otherwise unreachable.</exception>
    /// <exception cref="UnauthorizedAccessException">The file is on a read-only volume, or this process may not write it.</exception>
    public static int? MigrateFile(string path, IReadOnlyList<ConfigurationMigration> chain)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(chain);

        // Path.Exists rather than File.Exists. A directory sitting where the
        // settings file belongs is not a fresh installation, and answering as
        // though it were would pass over it in silence; letting the load below
        // fail reports it instead.
        if (!Path.Exists(path))
        {
            return null;
        }

        // XElement rather than XDocument, because a document has a root that
        // the compiler believes may be absent and a load that succeeded never
        // produces one. Guarding against it would add a branch no fixture can
        // reach, and an unreachable branch is a line the suite cannot speak for.
        var root = XElement.Load(path);
        var from = Migrate(root, chain);

        if (from is null)
        {
            return null;
        }

        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(path);

        return from;
    }

    /// <summary>
    /// Reads the shape version a stored configuration file carries.
    /// </summary>
    /// <remarks>
    /// A file that is absent or unreadable answers with the current version
    /// rather than with zero, and the direction is deliberate. This is asked
    /// only by the write guard, whose question is whether writing would destroy
    /// something newer, and a file nobody can read is not evidence that it
    /// would. Refusing every save on an unreadable file would leave an operator
    /// with a settings page that will not save and no way to repair it from the
    /// page.
    /// </remarks>
    /// <param name="path">The stored configuration file.</param>
    /// <param name="whereUnreadable">The version to answer with where the file cannot be read.</param>
    /// <returns>The stored version.</returns>
    public static int VersionOfFile(string path, int whereUnreadable)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            return VersionOf(XElement.Load(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return whereUnreadable;
        }
    }

    /// <summary>
    /// Writes the version onto the stored configuration.
    /// </summary>
    /// <param name="root">The root element of the stored file.</param>
    /// <param name="version">The version to stamp.</param>
    private static void Stamp(XElement root, int version)
    {
        var text = version.ToString(CultureInfo.InvariantCulture);
        var element = root.Element(VersionElementName);

        if (element is null)
        {
            // First, so somebody opening the file sees which shape it is in
            // before they read the settings. The serializer puts it wherever
            // the property sits in the model when the server writes the file
            // back, and neither position changes what is read.
            root.AddFirst(new XElement(VersionElementName, text));
        }
        else
        {
            element.Value = text;
        }
    }
}
