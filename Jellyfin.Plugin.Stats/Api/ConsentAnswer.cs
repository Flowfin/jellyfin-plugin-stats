namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// What a person is saying about being named.
/// </summary>
/// <remarks>
/// Two fields rather than one, because an agreement is to a particular set of
/// words. A body carrying only yes or no would record an agreement to whatever
/// the server happened to ship at that moment, which may not be what the page
/// put in front of the person. Issue #42.
/// </remarks>
public sealed record ConsentAnswer
{
    /// <summary>
    /// Gets a value indicating whether the person is agreeing.
    /// </summary>
    public bool Agreed { get; init; }

    /// <summary>
    /// Gets the version of the wording the person was shown.
    /// </summary>
    /// <remarks>
    /// Read only where <see cref="Agreed"/> is true. A withdrawal is not an
    /// agreement to anything and names no version.
    /// </remarks>
    public int WordingVersion { get; init; }
}
