using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Every shape this plugin's stored configuration has ever had, in order.
/// </summary>
/// <remarks>
/// Version zero is the shape a file has when it carries no version at all,
/// which is every configuration file written before this landed. Each entry
/// below moves a file one version forward, and the position of an entry is the
/// version it moves away from, so <see cref="Current"/> is the length of the
/// list rather than a number kept beside it. A constant kept beside the list is
/// a constant somebody forgets to raise, and the file it forgets about is then
/// stamped with a version that never ran its own migration.
/// <para>
/// Adding a shape change is adding an entry at the end. Never edit an entry
/// that has shipped: a file written by the released plugin is stamped with a
/// version, and the meaning of that stamp is what the entries before it did.
/// </para>
/// </remarks>
public static class ConfigurationMigrations
{
    /// <summary>
    /// Gets the steps, oldest first.
    /// </summary>
    public static IReadOnlyList<ConfigurationMigration> All { get; } =
    [
        new ConfigurationMigration(
            "the template's example settings are dropped",
            root =>
            {
                // The upstream plugin template shipped four settings this
                // plugin never had a use for. They are removed rather than
                // left in place: the serializer ignores an element it has no
                // property for, so a file keeping them would load correctly
                // and go on carrying values that mean nothing, and the next
                // person reading that file would have to work out which of the
                // settings in it the plugin actually reads.
                //
                // Nothing is carried forward from them, because no setting this
                // plugin has is the same setting under a new name. Where that
                // is not true of a later change, the step carries the value
                // across before removing the old element.
                foreach (var name in new[] { "TrueFalseSetting", "AnInteger", "AString", "Options" })
                {
                    root.Elements(name).Remove();
                }
            })
    ];

    /// <summary>
    /// Gets the shape version this plugin writes.
    /// </summary>
    public static int Current => All.Count;

    /// <summary>
    /// Says what a run of the chain over a given version would do.
    /// </summary>
    /// <remarks>
    /// Built here rather than in the caller so that the sentence exists once,
    /// and so it names the steps that ran rather than only the two numbers.
    /// Two numbers alone do not tell an administrator reading a log whether the
    /// upgrade touched the setting they are looking at.
    /// </remarks>
    /// <param name="from">The version the stored file was at.</param>
    /// <returns>What the steps from that version do, joined into one clause.</returns>
    public static string Describe(int from)
    {
        return string.Join("; ", All.Skip(from).Select(step => step.Describes));
    }
}
