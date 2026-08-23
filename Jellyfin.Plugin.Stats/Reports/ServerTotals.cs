using System;
using Jellyfin.Plugin.Stats.Aggregation;

namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// What a range of time comes to, as one answer that names nobody.
/// </summary>
/// <remarks>
/// The simplest of the five shapes, and the one every page in the dashboard
/// carries at least one of. It is a total and not a breakdown: there is no key
/// on it to group by, so there is nothing on it for a reader to attribute to a
/// person however small the server is.
/// <para>
/// That is also why it stays available when a breakdown does not. Issue #41's
/// rule refuses the pairing of a total with an incomplete breakdown rather than
/// the total, because what leaks is the subtraction between them and a total on
/// its own is not one half of anything.
/// </para>
/// <para>
/// Issue #51.
/// </para>
/// </remarks>
/// <param name="Plays">How many plays fell in the range.</param>
/// <param name="Watched">How long was watched across them.</param>
/// <param name="Delivery">How those plays were delivered, in the four figures that add up to <paramref name="Plays"/>.</param>
public sealed record ServerTotals(long Plays, TimeSpan Watched, DeliveryMethodShares Delivery);
