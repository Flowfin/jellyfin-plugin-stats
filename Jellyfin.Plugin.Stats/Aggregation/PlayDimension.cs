namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// The things a set of plays may be broken down by.
/// </summary>
/// <remarks>
/// A closed set rather than a column name or a selector handed in. Issue #55
/// asks that a filter or a sort chosen by a caller map through a closed set, and
/// a breakdown chooses which column it groups on, which is the same choice one
/// step further in. An enumeration is the smallest thing that cannot be widened
/// by a request.
/// <para>
/// The user is deliberately absent and is not an omission to be filled in later.
/// Issue #41 holds why: a breakdown that can group by user is the thing the
/// consent rule exists in front of, and a set that cannot name one cannot be
/// asked to. How the plays were delivered is absent for a different reason -
/// that answer has exactly one value per play and is
/// <see cref="DeliveryMethodShares"/>, which every row below already carries.
/// </para>
/// </remarks>
public enum PlayDimension
{
    /// <summary>
    /// The client application the play came from, as it named itself.
    /// </summary>
    Client,

    /// <summary>
    /// The device the play came from. Plays are grouped by the identifier the
    /// server gave the device and labelled with the name it reported, so a
    /// device that was renamed stays one device.
    /// </summary>
    Device
}
