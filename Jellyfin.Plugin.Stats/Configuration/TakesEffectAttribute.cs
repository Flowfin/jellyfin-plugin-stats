using System;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Says when a change to the field this is written on takes effect.
/// </summary>
/// <remarks>
/// Written on the field rather than kept in a list somewhere else, so a field
/// added to the model arrives with the answer or arrives without it and is
/// refused by the suite. A list would go stale in the one direction that matters,
/// which is a new setting nobody classified.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class TakesEffectAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TakesEffectAttribute"/> class.
    /// </summary>
    /// <param name="when">When a change to the field takes effect.</param>
    public TakesEffectAttribute(WhenAChangeTakesEffect when)
    {
        When = when;
    }

    /// <summary>
    /// Gets when a change to the field takes effect.
    /// </summary>
    public WhenAChangeTakesEffect When { get; }
}
