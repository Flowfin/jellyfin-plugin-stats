// The years a wrap-up's selector may offer, driven through the endpoint that
// answers them. Issue #67's third condition.
//
// What the store answers is driven where the store is; what this is about is the
// route between it and a page, and the second fact that travels with the list:
// the day rows are still kept from, without which a reader meeting a gap cannot
// tell a year the account recorded nothing in from a year the sweep has been
// through.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class TheYearsAWrapUpMayOfferTests
{
    /// <summary>
    /// The moment every case here runs at, fixed rather than read off a clock so
    /// the day rows are kept from is a value a case chose.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The years an account has, answered as the store would.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheAnswerCarriesTheYearsTheStoreHoldsForThatAccount()
    {
        using var endpoints = Over(2023, 2025, 2026);

        var answer = await endpoints.Get(Path(Caller.Someone), Caller.Someone);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(
            new[] { 2023, 2025, 2026 },
            Years(body));
    }

    /// <summary>
    /// The run between the first year and the last is not filled in. A selector
    /// that offered every year in the span would put a year the account has
    /// nothing in front of them to be opened empty.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AYearTheAccountHasNothingInIsNotOffered()
    {
        using var endpoints = Over(2023, 2026);

        using var body = JsonDocument.Parse((await endpoints.Get(Path(Caller.Someone), Caller.Someone)).Body);

        Assert.DoesNotContain(2024, Years(body));
        Assert.DoesNotContain(2025, Years(body));
    }

    /// <summary>
    /// The day rows are kept from travels with the list, measured from the
    /// server's clock and the retention setting rather than left to the page.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheAnswerSaysWhichDayRowsAreStillKeptFrom()
    {
        using var endpoints = Over(new PluginConfiguration { PlayRowRetentionDays = 90 }, 2026);

        using var body = JsonDocument.Parse((await endpoints.Get(Path(Caller.Someone), Caller.Someone)).Body);

        Assert.Equal(
            Now.UtcDateTime.AddDays(-90).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            body.RootElement.GetProperty("keptFrom").GetString());
    }

    /// <summary>
    /// A different retention setting is a different day, read while the request
    /// is served rather than at start-up. A page told the wrong edge would
    /// explain a gap as a sweep that has not happened.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheDayMovesWithTheSetting()
    {
        using var endpoints = Over(new PluginConfiguration { PlayRowRetentionDays = 400 }, 2026);

        using var body = JsonDocument.Parse((await endpoints.Get(Path(Caller.Someone), Caller.Someone)).Body);

        Assert.Equal(
            Now.UtcDateTime.AddDays(-400).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            body.RootElement.GetProperty("keptFrom").GetString());
    }

    /// <summary>
    /// An account with nothing recorded is answered with no years rather than
    /// with a refusal. Having watched nothing is a fact about a person and not
    /// an error, and the drawing is what says so in words.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AnAccountWithNothingRecordedIsAnsweredWithNoYears()
    {
        using var endpoints = Over();

        var answer = await endpoints.Get(Path(Caller.Someone), Caller.Someone);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Empty(Years(body));
    }

    /// <summary>
    /// A store that will not open leaves here as an outage rather than as an
    /// account that has watched nothing. The two are the same empty list to
    /// anything that could not tell them apart.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAnEmptyHistory()
    {
        using var endpoints = new InProcessEndpoints(
            clock: new FixedClock(Now),
            held: (userId, zone) => throw new Jellyfin.Plugin.Stats.Data.StoreCouldNotBeOpenedException(
                "the store is not there",
                new System.IO.IOException("the store is not there")));

        Assert.Equal(503, (await endpoints.Get(Path(Caller.Someone), Caller.Someone)).Status);
    }

    /// <summary>
    /// The years are read in the zone the settings name and never in the
    /// machine's. A play in the last hours of December belongs to one year or
    /// the next depending on whose midnight is meant.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheYearsAreReadInTheZoneTheSettingsName()
    {
        var asked = new List<string>();

        using var endpoints = new InProcessEndpoints(
            configuration: new PluginConfiguration { RollupTimeZone = "Pacific/Auckland" },
            clock: new FixedClock(Now),
            held: (userId, zone) =>
            {
                asked.Add(zone.Id);
                return [2026];
            });

        await endpoints.Get(Path(Caller.Someone), Caller.Someone);

        Assert.Equal("Pacific/Auckland", Assert.Single(asked));
    }

    /// <summary>
    /// The account whose years are read is the account the server says is
    /// asking, and never the one the route names.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheAccountReadIsTheOneMakingTheRequest()
    {
        var asked = new List<Guid>();

        using var endpoints = new InProcessEndpoints(
            clock: new FixedClock(Now),
            held: (userId, zone) =>
            {
                asked.Add(userId);
                return [2026];
            });

        await endpoints.Get(Path(Caller.Someone), Caller.Someone);

        Assert.Equal(Caller.Someone.UserId, Assert.Single(asked));
    }

    private static string Path(Caller caller)
        => "/Stats/Users/" + caller.UserId.ToString("D", CultureInfo.InvariantCulture) + "/Years";

    private static int[] Years(JsonDocument body)
    {
        var held = body.RootElement.GetProperty("held");
        var years = new int[held.GetArrayLength()];

        for (var i = 0; i < years.Length; i++)
        {
            years[i] = held[i].GetInt32();
        }

        return years;
    }

    private static InProcessEndpoints Over(params int[] years)
        => Over(new PluginConfiguration(), years);

    private static InProcessEndpoints Over(PluginConfiguration configuration, params int[] years)
        => new(
            configuration: configuration,
            clock: new FixedClock(Now),
            held: (userId, zone) => years);
}
