using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Xml.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Everything this plugin can be told.
/// </summary>
/// <remarks>
/// Each field validates where it is set rather than somewhere a caller has to
/// remember to call. The server reads a stored configuration file straight into
/// this type and hands the same type to the settings page, so a check that sits
/// beside those paths is a check one of them eventually skips; a check in the
/// setter runs on the stored file, on the page's save, and on an object a test
/// builds by hand, without any of the three knowing about it.
/// <para>
/// A value outside its range is refused and the field falls back to its default,
/// and the name of the field is added to <see cref="RejectedFields"/>. Refusing
/// rather than clamping is deliberate: a retention of eleven thousand days
/// clamped to ten years is a setting that silently means something other than
/// what it says, and the operator is never told.
/// </para>
/// <para>
/// A configuration file the server cannot parse at all never reaches this type.
/// The server catches that itself and writes a fresh default in its place, so
/// the case this handles is the other one, where the file parses and carries a
/// number nobody should have been able to enter.
/// </para>
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    private readonly SortedSet<string> _rejected = new(StringComparer.Ordinal);

    private bool _captureEnabled = ConfigurationLimits.DefaultCaptureEnabled;
    private int _playRowRetentionDays = ConfigurationLimits.DefaultPlayRowRetentionDays;
    private int _dailyAggregateRetentionDays = ConfigurationLimits.DefaultDailyAggregateRetentionDays;
    private int _maximumRangeDays = ConfigurationLimits.DefaultMaximumRangeDays;
    private int _maximumRowsPerResponse = ConfigurationLimits.DefaultMaximumRowsPerResponse;
    private string _rollupTimeZone = ConfigurationLimits.DefaultRollupTimeZone;
    private string[] _excludedUserIds = [];
    private string[] _excludedItemTypes = [];

    /// <summary>
    /// Gets or sets the shape this configuration was stored in.
    /// </summary>
    /// <remarks>
    /// Not a setting. It is written by the plugin and read by the plugin, it is
    /// not on the settings page, and an operator changing it by hand would only
    /// be telling the plugin a lie about which migrations have run. It sits on
    /// the model rather than being kept beside the file because the server
    /// serializes this type over the whole file, so a stamp the model did not
    /// carry would be deleted by the first save and the file would look like a
    /// pre-stamp one again on the next start.
    /// <para>
    /// It starts at the current version because a configuration this plugin
    /// builds for itself is by definition in the shape this plugin writes. A
    /// stored file that is older than that never reaches the default: the
    /// migrator moves the file forward before the server reads it, so the value
    /// that lands here is the one the file was moved to.
    /// </para>
    /// </remarks>
    [TakesEffect(WhenAChangeTakesEffect.NotASetting)]
    public int ConfigurationVersion { get; set; } = ConfigurationMigrations.Current;

    /// <summary>
    /// Gets or sets a value indicating whether plays are recorded at all.
    /// </summary>
    /// <remarks>
    /// Off means nothing is written, not that nothing is shown. Where the switch
    /// is honoured is issue #39, and it belongs immediately before the write.
    /// </remarks>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public bool CaptureEnabled
    {
        get => _captureEnabled;
        set => _captureEnabled = value;
    }

    /// <summary>
    /// Gets or sets how many days of raw play rows are kept.
    /// </summary>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public int PlayRowRetentionDays
    {
        get => _playRowRetentionDays;
        set => _playRowRetentionDays = Accepted(
            nameof(PlayRowRetentionDays),
            value,
            ConfigurationLimits.IsAcceptableDayCount,
            ConfigurationLimits.DefaultPlayRowRetentionDays);
    }

    /// <summary>
    /// Gets or sets how many days of daily aggregates are kept.
    /// </summary>
    /// <remarks>
    /// Longer than the raw rows on purpose. The rows answer who watched what and
    /// the aggregates answer how much the server was used, and the second
    /// question can be answered for longer without keeping the first one's data.
    /// </remarks>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public int DailyAggregateRetentionDays
    {
        get => _dailyAggregateRetentionDays;
        set => _dailyAggregateRetentionDays = Accepted(
            nameof(DailyAggregateRetentionDays),
            value,
            ConfigurationLimits.IsAcceptableDayCount,
            ConfigurationLimits.DefaultDailyAggregateRetentionDays);
    }

    /// <summary>
    /// Gets or sets the widest range a report may ask for, in days.
    /// </summary>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public int MaximumRangeDays
    {
        get => _maximumRangeDays;
        set => _maximumRangeDays = Accepted(
            nameof(MaximumRangeDays),
            value,
            ConfigurationLimits.IsAcceptableDayCount,
            ConfigurationLimits.DefaultMaximumRangeDays);
    }

    /// <summary>
    /// Gets or sets the most rows any single response may carry.
    /// </summary>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public int MaximumRowsPerResponse
    {
        get => _maximumRowsPerResponse;
        set => _maximumRowsPerResponse = Accepted(
            nameof(MaximumRowsPerResponse),
            value,
            ConfigurationLimits.IsAcceptableRowCap,
            ConfigurationLimits.DefaultMaximumRowsPerResponse);
    }

    /// <summary>
    /// Gets or sets the zone days are counted in.
    /// </summary>
    /// <remarks>
    /// A zone the running machine cannot resolve is refused rather than kept.
    /// Keeping it would mean every rollup afterwards throws, and the setting
    /// that caused it would still be sitting on the page looking correct.
    /// </remarks>
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public string RollupTimeZone
    {
        get => _rollupTimeZone;
        set => _rollupTimeZone = Accepted(
            nameof(RollupTimeZone),
            value,
            ConfigurationLimits.IsAcceptableTimeZone,
            ConfigurationLimits.DefaultRollupTimeZone);
    }

    /// <summary>
    /// Gets or sets the users whose plays are not recorded, by identifier.
    /// </summary>
    /// <remarks>
    /// One bad entry does not throw the rest away. An exclusion list is a list
    /// of people who asked not to be recorded, and discarding the whole list
    /// because one line is malformed starts recording all of them.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The server reads and writes this type with XmlSerializer, which round-trips an array property and cannot populate a read-only collection one. A shape the storage layer cannot carry is not a shape this setting can have.")]
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public string[] ExcludedUserIds
    {
        get => _excludedUserIds;
        set => _excludedUserIds = Kept(nameof(ExcludedUserIds), value, ConfigurationLimits.IsAcceptableUserId);
    }

    /// <summary>
    /// Gets or sets the item types whose plays are not recorded.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The server reads and writes this type with XmlSerializer, which round-trips an array property and cannot populate a read-only collection one. A shape the storage layer cannot carry is not a shape this setting can have.")]
    [TakesEffect(WhenAChangeTakesEffect.AtOnce)]
    public string[] ExcludedItemTypes
    {
        get => _excludedItemTypes;
        set => _excludedItemTypes = Kept(nameof(ExcludedItemTypes), value, ConfigurationLimits.IsAcceptableItemType);
    }

    /// <summary>
    /// Gets the fields whose stored value was refused, in name order.
    /// </summary>
    /// <remarks>
    /// Not stored. It describes the file that was read rather than belonging in
    /// it, and writing it back would make the next load report a rejection that
    /// had already been repaired. The settings page reads it off the same object
    /// the server hands it and says which fields went back to their defaults,
    /// which is the only place an operator would otherwise have to read a log to
    /// find out.
    /// </remarks>
    [XmlIgnore]
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The server reads and writes this type with XmlSerializer, which round-trips an array property and cannot populate a read-only collection one. A shape the storage layer cannot carry is not a shape this setting can have.")]
    [TakesEffect(WhenAChangeTakesEffect.NotASetting)]
    public string[] RejectedFields => [.. _rejected];

    /// <summary>
    /// Gets why this plugin's store could not be opened, or an empty string
    /// where nothing has failed to open it.
    /// </summary>
    /// <remarks>
    /// Not stored, and not a setting. It is what the plugin is doing rather
    /// than what it was told, and it travels here for the reason
    /// <see cref="RejectedFields"/> does: the settings page is handed this
    /// object and nothing else, so a state the page has to show has to be on
    /// it. Issues #31 and #65.
    /// <para>
    /// An empty string rather than a null reference, because the page reads
    /// this out of JSON and a field that is sometimes absent is a field the
    /// page has to test for two ways.
    /// </para>
    /// </remarks>
    [XmlIgnore]
    [TakesEffect(WhenAChangeTakesEffect.NotASetting)]
    public string WhyTheStoreCouldNotBeOpened => PluginState.Current.WhyTheStoreCouldNotBeOpened ?? string.Empty;

    /// <summary>
    /// Gets when the oldest play this plugin still holds started, as an
    /// instant, or an empty string where it holds none or could not be read.
    /// </summary>
    /// <remarks>
    /// Not stored, and not a setting, for the same reason as the field above.
    /// <para>
    /// The instant is written out whole and is not formatted here. A date on a
    /// settings page is read by somebody in their own zone, and this plugin
    /// counts days in the zone <see cref="RollupTimeZone"/> names, so a string
    /// formatted in the server process would be a third answer belonging to
    /// neither of them.
    /// </para>
    /// <para>
    /// Empty says two different things and the field above is what tells them
    /// apart: a store that was read and holds nothing, and a store that could
    /// not be read at all. A page that reported the first without looking at
    /// the second would tell an operator with a broken store that their server
    /// has never played anything.
    /// </para>
    /// </remarks>
    [XmlIgnore]
    [TakesEffect(WhenAChangeTakesEffect.NotASetting)]
    public string OldestStoredPlay =>
        PluginState.Current.OldestPlayStartedUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Describes what this plugin refused, in one line, or an empty string.
    /// </summary>
    /// <remarks>
    /// Built here rather than in the page so the sentence exists for anything
    /// else that has to report it, and so the page carries no text about a field
    /// it might not have been updated to know about.
    /// </remarks>
    /// <returns>A sentence naming the refused fields, or an empty string.</returns>
    public string DescribeRejections()
    {
        if (_rejected.Count == 0)
        {
            return string.Empty;
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "The stored value of {0} was outside what this plugin accepts, so the default is being used instead. Saving this page writes the values shown here.",
            string.Join(", ", _rejected));
    }

    /// <summary>
    /// Takes a value where it is acceptable, and the default where it is not.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="field">The name of the field being set.</param>
    /// <param name="value">The value being set.</param>
    /// <param name="acceptable">What the field accepts.</param>
    /// <param name="fallback">The value used where the field refuses.</param>
    /// <returns>The value to store.</returns>
    private T Accepted<T>(string field, T value, Func<T, bool> acceptable, T fallback)
    {
        if (acceptable(value))
        {
            _rejected.Remove(field);
            return value;
        }

        _rejected.Add(field);
        return fallback;
    }

    /// <summary>
    /// Keeps the acceptable entries of a list and refuses the rest.
    /// </summary>
    /// <param name="field">The name of the field being set.</param>
    /// <param name="value">The value being set.</param>
    /// <param name="acceptable">What an entry has to be.</param>
    /// <returns>The entries to store.</returns>
    private string[] Kept(string field, string[]? value, Func<string?, bool> acceptable)
    {
        var entries = value ?? [];
        var kept = entries.Where(acceptable).ToArray();

        if (kept.Length == entries.Length)
        {
            _rejected.Remove(field);
        }
        else
        {
            _rejected.Add(field);
        }

        return kept;
    }
}
