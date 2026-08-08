using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Jellyfin.Plugin.Stats.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// Every setting this plugin has, its default, its accepted range and one value
/// it refuses. A setting that accepts a nonsensical value is not a fault that
/// shows up at the moment somebody types it: it shows up much later, as a report
/// that is wrong or empty for a reason nobody can trace back to a text box.
/// <para>
/// The checks read the model rather than a list written beside it wherever they
/// can, so a field added without a default, or without a place in the page, is a
/// red test rather than a gap. Issue #71.
/// </para>
/// </summary>
public class ConfigurationModelTests
{
    /// <summary>
    /// The settings an operator can change, and what each one does with a value
    /// it will not take. Every writable property of the model has to appear
    /// here, which is asserted below rather than trusted.
    /// </summary>
    /// <returns>One case per setting.</returns>
    public static TheoryData<string, object, object, object?> Settings()
    {
        return new TheoryData<string, object, object, object?>
        {
            // field, default, an accepted value, one refused value
            { nameof(PluginConfiguration.PlayRowRetentionDays), 90, 3650, 0 },
            { nameof(PluginConfiguration.DailyAggregateRetentionDays), 400, 1, 3651 },
            { nameof(PluginConfiguration.MaximumRangeDays), 400, 365, -1 },
            { nameof(PluginConfiguration.MaximumRowsPerResponse), 1000, 100000, 100001 },
            { nameof(PluginConfiguration.RollupTimeZone), "UTC", "Europe/Berlin", "Nowhere/Atall" },
            {
                nameof(PluginConfiguration.ExcludedUserIds),
                Array.Empty<string>(),
                new[] { "f8b41e2c-9a17-4d63-8f0a-5c2e7b41d9aa" },
                new[] { "not-an-identifier" }
            },
            {
                nameof(PluginConfiguration.ExcludedItemTypes),
                Array.Empty<string>(),
                new[] { "Movie", "Episode" },
                new[] { "Filmstrip" }
            },

            // A boolean has no value outside its range, so there is nothing to
            // refuse and the last column says so rather than inventing one.
            { nameof(PluginConfiguration.CaptureEnabled), true, false, null }
        };
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySettingStartsAtItsDefault(string field, object expected, object accepted, object? refused)
    {
        _ = accepted;
        _ = refused;

        Assert.Equal(expected, Property(field).GetValue(new PluginConfiguration()));
    }

    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySettingTakesAValueInsideItsRange(string field, object expected, object accepted, object? refused)
    {
        _ = expected;
        _ = refused;

        var configuration = new PluginConfiguration();
        Property(field).SetValue(configuration, accepted);

        Assert.Equal(accepted, Property(field).GetValue(configuration));
        Assert.DoesNotContain(field, configuration.RejectedFields);
    }

    /// <summary>
    /// A refused value does not survive, the field goes back to its default, and
    /// the field is named. All three matter: a refusal nobody is told about is a
    /// setting that silently means something else, which is the failure the
    /// whole model exists against.
    /// </summary>
    /// <param name="field">The setting.</param>
    /// <param name="expected">Its default.</param>
    /// <param name="accepted">A value it takes, unused here.</param>
    /// <param name="refused">A value it refuses, or null where none exists.</param>
    [Theory]
    [MemberData(nameof(Settings))]
    public void EverySettingRefusesAValueOutsideItsRangeAndSaysSo(string field, object expected, object accepted, object? refused)
    {
        _ = accepted;

        if (refused is null)
        {
            return;
        }

        var configuration = new PluginConfiguration();
        Property(field).SetValue(configuration, refused);

        Assert.Equal(expected, Property(field).GetValue(configuration));
        Assert.Contains(field, configuration.RejectedFields);
        Assert.Contains(field, configuration.DescribeRejections());
    }

    /// <summary>
    /// The table above is the statement of what this plugin can be told, so a
    /// field added to the model without a row in it would be a setting with no
    /// default, no range and no refusal ever checked.
    /// </summary>
    [Fact]
    public void EverySettingOnTheModelHasACaseInTheTable()
    {
        var tabled = Settings().Select(row => (string)row[0]!).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SettableFields().OrderBy(name => name, StringComparer.Ordinal), tabled.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// Every setting is editable on the configuration page, found by the name the
    /// page's script reads it under. A setting that cannot be reached from the
    /// dashboard is a setting only somebody editing XML by hand can change.
    /// </summary>
    [Fact]
    public void EverySettingIsEditableOnTheConfigurationPage()
    {
        var page = EmbeddedConfigurationPage();

        foreach (var field in SettableFields())
        {
            Assert.Contains("id=\"" + field + "\"", page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The page shows what was refused. The element and the property that fills
    /// it are both named here, because a page that reads a property the model
    /// does not have shows nothing and looks fine.
    /// </summary>
    [Fact]
    public void TheConfigurationPageShowsWhichFieldsWereRefused()
    {
        var page = EmbeddedConfigurationPage();

        Assert.Contains("id=\"StatsRejectedFields\"", page, StringComparison.Ordinal);
        Assert.Contains("config." + nameof(PluginConfiguration.RejectedFields), page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The upstream template's three example settings are gone from the model and
    /// from the page.
    /// </summary>
    [Fact]
    public void TheTemplatesExampleSettingsAreGone()
    {
        var page = EmbeddedConfigurationPage();

        foreach (var example in new[] { "TrueFalseSetting", "AnInteger", "AString", "SomeOptions" })
        {
            Assert.Null(typeof(PluginConfiguration).GetProperty(example));
            Assert.DoesNotContain(example, page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A stored file that parses and carries values nobody should have been able
    /// to enter. It loads, every refused field is back at its default, every
    /// acceptable field is kept, and the refusals are named.
    /// </summary>
    /// <remarks>
    /// The server's own loader is not in the way here. It catches a file it
    /// cannot parse and writes a fresh default over it, which is the other case;
    /// a file that parses reaches this type unexamined, and this is what examines
    /// it. The same serializer the server uses is used here so the path is the
    /// real one.
    /// </remarks>
    [Fact]
    public void AStoredFileFullOfImpossibleValuesLoadsAndFallsBackFieldByField()
    {
        const string Stored = """
            <?xml version="1.0" encoding="utf-8"?>
            <PluginConfiguration xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <CaptureEnabled>false</CaptureEnabled>
              <PlayRowRetentionDays>0</PlayRowRetentionDays>
              <DailyAggregateRetentionDays>36500</DailyAggregateRetentionDays>
              <MaximumRangeDays>30</MaximumRangeDays>
              <MaximumRowsPerResponse>-5</MaximumRowsPerResponse>
              <RollupTimeZone>Middle/Earth</RollupTimeZone>
              <ExcludedUserIds>
                <string>f8b41e2c-9a17-4d63-8f0a-5c2e7b41d9aa</string>
                <string>everybody</string>
              </ExcludedUserIds>
              <ExcludedItemTypes>
                <string>Movie</string>
              </ExcludedItemTypes>
            </PluginConfiguration>
            """;

        using var reader = new StringReader(Stored);
        var loaded = (PluginConfiguration)new XmlSerializer(typeof(PluginConfiguration)).Deserialize(reader)!;

        Assert.False(loaded.CaptureEnabled);
        Assert.Equal(ConfigurationLimits.DefaultPlayRowRetentionDays, loaded.PlayRowRetentionDays);
        Assert.Equal(ConfigurationLimits.DefaultDailyAggregateRetentionDays, loaded.DailyAggregateRetentionDays);
        Assert.Equal(30, loaded.MaximumRangeDays);
        Assert.Equal(ConfigurationLimits.DefaultMaximumRowsPerResponse, loaded.MaximumRowsPerResponse);
        Assert.Equal(ConfigurationLimits.DefaultRollupTimeZone, loaded.RollupTimeZone);
        Assert.Equal(["f8b41e2c-9a17-4d63-8f0a-5c2e7b41d9aa"], loaded.ExcludedUserIds);
        Assert.Equal(["Movie"], loaded.ExcludedItemTypes);

        Assert.Equal(
            [
                nameof(PluginConfiguration.DailyAggregateRetentionDays),
                nameof(PluginConfiguration.ExcludedUserIds),
                nameof(PluginConfiguration.MaximumRowsPerResponse),
                nameof(PluginConfiguration.PlayRowRetentionDays),
                nameof(PluginConfiguration.RollupTimeZone)
            ],
            loaded.RejectedFields);
    }

    /// <summary>
    /// What was refused describes the file that was read, so it is not written
    /// back into it. Written back, the next load would report a rejection that
    /// had already been repaired, and it would never stop.
    /// </summary>
    [Fact]
    public void WhatWasRefusedIsNotWrittenBackIntoTheStoredFile()
    {
        var configuration = new PluginConfiguration { PlayRowRetentionDays = 0 };
        Assert.NotEmpty(configuration.RejectedFields);

        using var written = new StringWriter();
        new XmlSerializer(typeof(PluginConfiguration)).Serialize(written, configuration);

        Assert.DoesNotContain(nameof(PluginConfiguration.RejectedFields), written.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The all-zero identifier is refused. It parses, so a check that only asked
    /// whether the text is an identifier would take it, and it names nobody: an
    /// exclusion list holding it excludes no user while looking like it does.
    /// </summary>
    [Fact]
    public void TheEmptyIdentifierIsNotAUserThatCanBeExcluded()
    {
        var configuration = new PluginConfiguration
        {
            ExcludedUserIds = ["00000000-0000-0000-0000-000000000000"]
        };

        Assert.Empty(configuration.ExcludedUserIds);
        Assert.Contains(nameof(PluginConfiguration.ExcludedUserIds), configuration.RejectedFields);
    }

    /// <summary>
    /// Blank entries and a blank zone are refused rather than carried. An empty
    /// element in a stored file arrives as an empty string, which is the shape
    /// this covers, and a zone of nothing at all would otherwise reach the
    /// lookup.
    /// </summary>
    /// <param name="blank">A stored value carrying nothing.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankValueIsRefusedWhereTextIsExpected(string blank)
    {
        var configuration = new PluginConfiguration
        {
            RollupTimeZone = blank,
            ExcludedItemTypes = [blank]
        };

        Assert.Equal(ConfigurationLimits.DefaultRollupTimeZone, configuration.RollupTimeZone);
        Assert.Empty(configuration.ExcludedItemTypes);
        Assert.Contains(nameof(PluginConfiguration.RollupTimeZone), configuration.RejectedFields);
        Assert.Contains(nameof(PluginConfiguration.ExcludedItemTypes), configuration.RejectedFields);
    }

    /// <summary>
    /// A list element absent from a stored file arrives as a null reference
    /// rather than as an empty list. That is not a refusal, it is the setting
    /// having no entries, and treating it as a refusal would report a rejection
    /// against every configuration that never set the field.
    /// </summary>
    [Fact]
    public void AListThatIsNotThereAtAllIsEmptyAndIsNotARefusal()
    {
        var configuration = new PluginConfiguration
        {
            ExcludedUserIds = null!,
            ExcludedItemTypes = null!
        };

        Assert.Empty(configuration.ExcludedUserIds);
        Assert.Empty(configuration.ExcludedItemTypes);
        Assert.Empty(configuration.RejectedFields);
    }

    /// <summary>
    /// Repairing a field clears its name again, so the page stops reporting a
    /// refusal the operator has already dealt with.
    /// </summary>
    [Fact]
    public void RepairingAFieldClearsItsRefusal()
    {
        var configuration = new PluginConfiguration { PlayRowRetentionDays = 0 };
        Assert.Contains(nameof(PluginConfiguration.PlayRowRetentionDays), configuration.RejectedFields);

        configuration.PlayRowRetentionDays = 30;

        Assert.Empty(configuration.RejectedFields);
        Assert.Equal(string.Empty, configuration.DescribeRejections());
    }

    /// <summary>
    /// Names the settings an operator can change.
    /// </summary>
    /// <returns>The writable property names of the model.</returns>
    private static IEnumerable<string> SettableFields()
    {
        return typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .Select(property => property.Name);
    }

    /// <summary>
    /// Reads a property of the model by name.
    /// </summary>
    /// <param name="field">The property name.</param>
    /// <returns>The property.</returns>
    private static PropertyInfo Property(string field)
    {
        var property = typeof(PluginConfiguration).GetProperty(field);
        Assert.True(property is not null, "The model has no property called " + field + ".");
        return property!;
    }

    /// <summary>
    /// Reads the configuration page out of the compiled plugin assembly.
    /// </summary>
    /// <returns>The text of the embedded configuration page.</returns>
    private static string EmbeddedConfigurationPage()
    {
        var pluginType = typeof(Plugin);
        var name = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}.Configuration.configPage.html",
            pluginType.Namespace);

        using var stream = pluginType.Assembly.GetManifestResourceStream(name);
        Assert.True(stream is not null, "The assembly embeds no resource named " + name + ".");

        using var reader = new StreamReader(stream!);
        return reader.ReadToEnd();
    }
}
