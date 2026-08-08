using System;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// One step from a stored configuration shape to the next one.
/// </summary>
/// <remarks>
/// A step carries no version number of its own. Where it sits in
/// <see cref="ConfigurationMigrations.All"/> is the version it moves a file
/// away from, so a chain cannot be built with a gap in it or with two steps
/// claiming the same version. A number written on the step as well would be a
/// second opinion about the same fact, and the failure it invites is silent:
/// a step numbered 3 sitting in position 2 skips the file it was written for.
/// <para>
/// The step is handed the root element rather than a parsed configuration
/// object, because the whole reason this exists is that a renamed setting is
/// invisible to the parsed object. The serializer drops an element the current
/// type has no property for, so a step that read the object would be reading a
/// value that has already been thrown away.
/// </para>
/// </remarks>
public sealed class ConfigurationMigration
{
    private readonly Action<XElement> _apply;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationMigration"/> class.
    /// </summary>
    /// <param name="describes">What changed about the shape, for the log line and for a reader of the chain.</param>
    /// <param name="apply">What the step does to the root element.</param>
    public ConfigurationMigration(string describes, Action<XElement> apply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(describes);
        ArgumentNullException.ThrowIfNull(apply);

        Describes = describes;
        _apply = apply;
    }

    /// <summary>
    /// Gets what this step changed about the stored shape.
    /// </summary>
    public string Describes { get; }

    /// <summary>
    /// Runs this step over a stored configuration.
    /// </summary>
    /// <param name="root">The root element of the stored file.</param>
    public void Apply(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        _apply(root);
    }
}
