using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Thrown where a save carried a value this plugin does not accept.
/// </summary>
/// <remarks>
/// A save is taken whole or not at all. The model refuses a bad value field by
/// field and falls that field back to its default, which is right for a stored
/// file that already carries one, because leaving it unchanged there means
/// keeping the bad value for as long as the file exists. It is wrong for
/// somebody typing into the settings page: the save appears to succeed, the
/// operator believes the value they entered is in force, and something else is.
/// <para>
/// So the load path keeps the field-by-field fallback and the write path stops
/// here. Which fields were refused is already recorded on the configuration
/// object by the setters, so nothing re-derives it and there is no second copy
/// of the limits to fall out of step with the first.
/// </para>
/// <para>
/// It carries the names rather than only a sentence, so a caller can report
/// them without parsing the message back apart. One constructor and none of the
/// three an exception type usually carries, for the reason the neighbouring
/// exception in this folder gives: it is thrown from one place, and a
/// constructor nothing calls is a line the suite cannot speak for.
/// </para>
/// </remarks>
public class ConfigurationValueRefusedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationValueRefusedException"/> class.
    /// </summary>
    /// <param name="fields">The fields whose value was refused, in name order.</param>
    public ConfigurationValueRefusedException(IReadOnlyList<string> fields)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "The value sent for {0} is outside what this plugin accepts, so nothing was saved and the stored settings are unchanged.",
            string.Join(", ", fields)))
    {
        Fields = [.. fields];
    }

    /// <summary>
    /// Gets the fields whose value was refused, in name order.
    /// </summary>
    public IReadOnlyList<string> Fields { get; }
}
