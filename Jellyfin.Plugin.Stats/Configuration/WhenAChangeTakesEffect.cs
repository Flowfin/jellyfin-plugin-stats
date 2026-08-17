namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// When a change to a field of <see cref="PluginConfiguration"/> starts to
/// matter.
/// </summary>
/// <remarks>
/// An operator who saves the page has no way to tell a value that is already in
/// force from one that will be read at the next start, and the difference is
/// invisible in both directions: capture left running after it was turned off
/// looks the same as capture that stopped, until somebody counts the rows. So
/// each field says which it is, here, beside the field rather than in prose that
/// nothing compares against the model.
/// <para>
/// The answer is a property of the consumers rather than of the field, and no
/// reading of this type derives it. What this enumeration does is make the
/// answer written down and comparable: the settings page carries the same
/// answer per field, and the suite refuses a disagreement.
/// </para>
/// </remarks>
public enum WhenAChangeTakesEffect
{
    /// <summary>
    /// Every consumer reads the value at the moment it uses it, so a saved
    /// change is in force for the next event, the next sweep or the next
    /// request, with no restart in between.
    /// </summary>
    /// <remarks>
    /// A field nothing reads yet answers this as a definition of what it will
    /// mean rather than as a description of behaviour that has been measured.
    /// docs/configuration.md says which fields are in that state and is the
    /// place that separates the two.
    /// </remarks>
    AtOnce,

    /// <summary>
    /// Something reads the value once while the server starts and keeps it, so
    /// a saved change does nothing until the server is restarted. A field
    /// answering this has to say so where it is edited.
    /// </summary>
    OnRestart,

    /// <summary>
    /// The question does not arise, because the field is not something an
    /// operator sets. The stored shape stamp and the report of what was refused
    /// are on this type because the server serializes the whole of it, and
    /// neither is offered on the page.
    /// </summary>
    NotASetting
}
