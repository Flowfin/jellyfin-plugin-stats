using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One day of use, and how the plays that fell on it were delivered.
/// </summary>
/// <remarks>
/// Nothing here identifies a person. That is a property of the type rather than
/// of the code that fills it in: there is no field for a user, so a series
/// cannot carry one by being written carelessly, and adding one is a change
/// somebody has to make on purpose and defend. <see cref="DimensionRow"/> is
/// the same decision taken for the same reason.
/// </remarks>
/// <param name="Day">
/// The calendar day, read in the zone the answer names. A day is not the same
/// interval for everybody, so the day alone says nothing without the zone
/// beside it, which is why <see cref="DailyUsage.ZoneId"/> travels with it.
/// </param>
/// <param name="Watched">
/// How much of the items was actually watched on this day. It is the sum of
/// what the rows recorded as watched, not the time their sessions were open: a
/// play that is paused runs on the clock and not on the item, which is what
/// <c>PlayRecord.WatchedDuration</c> already keeps apart.
/// </param>
/// <param name="Delivery">
/// How this day's plays divide between the ways the server delivered them, and
/// how many there were. It is the same four figures the whole range answers
/// with, so a day's transcoded share and the range's are read the same way
/// rather than being two definitions that drift.
/// </param>
public sealed record DailyUsageRow(DateOnly Day, TimeSpan Watched, DeliveryMethodShares Delivery);
