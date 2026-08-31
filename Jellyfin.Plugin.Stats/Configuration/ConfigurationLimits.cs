using System;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Every default and every accepted range this plugin has, in one place.
/// </summary>
/// <remarks>
/// The defaults are written here and nowhere else: <see cref="PluginConfiguration"/>
/// starts each field from these and falls each field back to these when a stored
/// value is refused, so moving one is a one-line change rather than a hunt for
/// the copy that was missed. A default written twice is a default that disagrees
/// with itself the first time only one of the two is edited.
/// </remarks>
public static class ConfigurationLimits
{
    /// <summary>
    /// Whether a fresh installation records plays.
    /// </summary>
    /// <remarks>
    /// On. A statistics plugin that records nothing until somebody finds the
    /// switch reports an empty first month and looks broken. What protects the
    /// people on the server is that nothing personal is readable by anybody
    /// else, not that nothing is written.
    /// </remarks>
    public const bool DefaultCaptureEnabled = true;

    /// <summary>
    /// How many days of raw play rows are kept by default.
    /// </summary>
    public const int DefaultPlayRowRetentionDays = 90;

    /// <summary>
    /// How many days of daily aggregates are kept by default.
    /// </summary>
    public const int DefaultDailyAggregateRetentionDays = 400;

    /// <summary>
    /// The widest range a report may ask for by default, in days.
    /// </summary>
    /// <remarks>
    /// The plugin's own longest range, expressed as a setting, so an
    /// installation nobody has configured is bounded by exactly the number that
    /// bounded it before this setting reached anything. It was 400 until issue
    /// #305 wired the setting to the report path, and 400 against a query layer
    /// that refused at 367 was a page number and a behaviour disagreeing by
    /// five weeks.
    /// <para>
    /// The two are held together by a case rather than by this sentence, since
    /// a number written in two places is a number that disagrees with itself
    /// the first time only one of them is edited.
    /// </para>
    /// </remarks>
    public const int DefaultMaximumRangeDays = 367;

    /// <summary>
    /// The most rows any single response may carry by default.
    /// </summary>
    public const int DefaultMaximumRowsPerResponse = 1000;

    /// <summary>
    /// The zone days are counted in by default.
    /// </summary>
    /// <remarks>
    /// UTC rather than the machine's zone. A default read off the server would
    /// make the same rows roll up differently on two servers, and reading the
    /// machine zone is refused by <c>no-ambient-clock</c> in any case.
    /// </remarks>
    public const string DefaultRollupTimeZone = "UTC";

    /// <summary>
    /// The smallest number of days any retention or range setting may be.
    /// </summary>
    /// <remarks>
    /// One rather than zero. Zero days of retention is a plugin that captures
    /// and immediately deletes, which is a way of turning capture off that
    /// leaves the switch saying it is on.
    /// </remarks>
    public const int MinimumDays = 1;

    /// <summary>
    /// The largest number of days any retention or range setting may be.
    /// </summary>
    /// <remarks>
    /// Ten years. Not a storage limit, which nothing here can know, but a bound
    /// that separates a long retention somebody meant from a number somebody
    /// typed an extra digit into.
    /// </remarks>
    public const int MaximumDays = 3650;

    /// <summary>
    /// The smallest response cap.
    /// </summary>
    public const int MinimumRowsPerResponse = 1;

    /// <summary>
    /// The largest response cap.
    /// </summary>
    public const int MaximumRowsPerResponse = 100000;

    /// <summary>
    /// Decides whether a number of days is one this plugin accepts.
    /// </summary>
    /// <param name="days">The number of days.</param>
    /// <returns>True where the value is inside the accepted range.</returns>
    public static bool IsAcceptableDayCount(int days) => days >= MinimumDays && days <= MaximumDays;

    /// <summary>
    /// Decides whether a response cap is one this plugin accepts.
    /// </summary>
    /// <param name="rows">The cap.</param>
    /// <returns>True where the value is inside the accepted range.</returns>
    public static bool IsAcceptableRowCap(int rows) => rows >= MinimumRowsPerResponse && rows <= MaximumRowsPerResponse;

    /// <summary>
    /// Decides whether a string names a user this plugin could exclude.
    /// </summary>
    /// <remarks>
    /// An identifier and never a name. A name is not stable, is not unique on
    /// every server, and is the thing this plugin spends the rest of its design
    /// keeping out of places it does not belong.
    /// </remarks>
    /// <param name="value">The stored value.</param>
    /// <returns>True where the value is a user identifier.</returns>
    public static bool IsAcceptableUserId(string? value) => Guid.TryParse(value, out var id) && id != Guid.Empty;

    /// <summary>
    /// Decides whether a string names an item type the server knows.
    /// </summary>
    /// <remarks>
    /// The closed set is the server's own <see cref="BaseItemKind"/> rather than
    /// a list written here. A list of item types in this repository is a list
    /// that goes stale against the server it filters, and the failure is silent:
    /// an exclusion nobody notices stopped matching.
    /// </remarks>
    /// <param name="value">The stored value.</param>
    /// <returns>True where the value names an item type.</returns>
    public static bool IsAcceptableItemType(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && Enum.TryParse<BaseItemKind>(value, ignoreCase: false, out _);
    }

    /// <summary>
    /// Decides whether a string names a zone this machine can resolve.
    /// </summary>
    /// <param name="value">The stored value.</param>
    /// <returns>True where the value names a resolvable zone.</returns>
    public static bool IsAcceptableTimeZone(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && TimeZoneInfo.TryFindSystemTimeZoneById(value, out _);
    }
}
