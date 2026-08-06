using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Which day a moment belongs to, and which moments a day covers, in one zone.
/// </summary>
/// <remarks>
/// Rows are stored in UTC, and a day is not the same interval for everybody. A
/// play that starts at half past eleven at night belongs to a different day
/// depending on whose midnight is meant, so the zone is named at every call
/// here rather than assumed. The prior art passes a numeric hour offset from
/// the browser on each request instead, which is wrong twice a year in any zone
/// with a summer change: the offset that was right when the page loaded is not
/// the offset that was right for the rows it is asking about.
/// <para>
/// Nothing here reads a clock or a setting. The zone arrives as an argument, so
/// the same call gives the same answer on any machine, and the setting that
/// decides which zone a rollup is produced in stays where the configuration
/// model is.
/// </para>
/// <para>
/// Days are not all 24 hours long. On the day a zone moves its clocks the
/// interval is 23 or 25 hours, which is why the end of a day is derived from
/// the start of the next one rather than from any duration.
/// </para>
/// </remarks>
public static class LocalDay
{
    /// <summary>
    /// The day a moment falls on, read in one zone.
    /// </summary>
    /// <param name="instantUtc">The moment, in UTC.</param>
    /// <param name="zone">The zone it is being read in.</param>
    /// <returns>The calendar day that moment belongs to in that zone.</returns>
    public static DateOnly Of(DateTimeOffset instantUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instantUtc, zone).DateTime);
    }

    /// <summary>
    /// The first moment of a day in one zone.
    /// </summary>
    /// <remarks>
    /// Midnight is not a moment that always exists. A zone that moves its
    /// clocks forward at midnight has no 00:00 on that day at all, and asking
    /// for it is an error rather than an hour that quietly means something
    /// else. The first moment that does exist is what a day starts at, so the
    /// search steps forward a minute at a time until it finds one.
    /// </remarks>
    /// <param name="day">The day.</param>
    /// <param name="zone">The zone it is a day in.</param>
    /// <returns>The moment that day begins, in UTC.</returns>
    public static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = day.ToDateTime(TimeOnly.MinValue);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        // The other direction, a local time that happens twice, needs no choice
        // made here. ConvertTimeToUtc reads an ambiguous time as standard time,
        // which is the later of the two, and the end of the day before and the
        // start of this one are the same call, so the two agree whichever it
        // picks and no play falls between them.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// The moment a day ends in one zone, which is the moment the next one
    /// begins.
    /// </summary>
    /// <remarks>
    /// Half open on purpose. A range that ended at the last moment of the day
    /// would either lose the plays in the final second or need a rule about
    /// which side a boundary row falls on, and that rule is the thing this
    /// exists to remove.
    /// </remarks>
    /// <param name="day">The day.</param>
    /// <param name="zone">The zone it is a day in.</param>
    /// <returns>The first moment after that day, in UTC.</returns>
    public static DateTimeOffset EndOf(DateOnly day, TimeZoneInfo zone) => StartOf(day.AddDays(1), zone);
}
