// Which midnight a report means, and where that answer comes from.
//
// A day is not the same interval for everybody. The rows are in UTC, and the
// zone that turns them into days is named once in the settings rather than sent
// by whoever is looking, so two people opening one report are shown the same
// days. Issue #50.
//
// The failure written against here is the quiet one. A folded year is kept from
// the first time it is asked for, and the zone is part of what it is filed
// under, so a changed setting cannot be handed the old answer. What that alone
// does not cover is the caller: something has to read the setting again to ask
// in the new zone at all, and a caller that read it once at start-up would go
// on asking in the old zone, hit the same key and be handed the same answer,
// with nothing anywhere saying the setting and the report disagree. So the
// change is driven through the endpoint rather than against the hold.
//
// The rows are built in memory and the fold is driven directly. No clock, no
// zone setting of this machine, no store and no socket.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class RollupZoneTests
{
    /// <summary>
    /// Half past eleven at night on the fifteenth of June, which is the
    /// sixteenth in Berlin and still the fifteenth in New York.
    /// </summary>
    private static readonly DateTime LateOnTheFifteenth = new DateTime(2025, 6, 15, 23, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// The second condition of issue #50. The zone a year is folded in is read
    /// out of the settings at the moment the request is served, so an
    /// administrator who changes it is answered in the new zone by the next
    /// request and without anything being restarted.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AChangedZoneSettingIsAnsweredInTheNewZoneOnTheNextRequest()
    {
        var settings = new PluginConfiguration { RollupTimeZone = "Europe/Berlin" };
        var asked = new List<string>();

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                asked.Add(zone.Id);
                return YearInReview.Over([], userId, year, zone, topCount, null);
            },
            configuration: settings);

        var who = Caller.Someone;

        Assert.Equal("Europe/Berlin", ZoneOf(await endpoints.Get(Path(who.UserId, 2025), who)));

        settings.RollupTimeZone = "Pacific/Auckland";

        Assert.Equal("Pacific/Auckland", ZoneOf(await endpoints.Get(Path(who.UserId, 2025), who)));

        Assert.Equal(new[] { "Europe/Berlin", "Pacific/Auckland" }, asked);
    }

    /// <summary>
    /// The other direction, and the one that makes the case above mean
    /// something. A year asked for twice under one setting is folded once, so
    /// the second fold above is the changed setting rather than a hold that
    /// never holds anything.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AYearAskedForTwiceUnderOneSettingIsFoldedOnce()
    {
        var settings = new PluginConfiguration { RollupTimeZone = "Europe/Berlin" };
        var asked = new List<string>();

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                asked.Add(zone.Id);
                return YearInReview.Over([], userId, year, zone, topCount, null);
            },
            configuration: settings);

        var who = Caller.Someone;

        await endpoints.Get(Path(who.UserId, 2025), who);
        await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal("Europe/Berlin", Assert.Single(asked));
    }

    /// <summary>
    /// The first condition of issue #50, driven the whole way rather than over
    /// the day reading alone. A play at half past eleven at night is a
    /// different day for a viewer two hours ahead of UTC and one four hours
    /// behind, and changing the setting moves it, which is what a rebuilt
    /// rollup means at the far end.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ThePlayNearMidnightChangesDayWhenTheSettingChanges()
    {
        var plays = new[] { APlay(LateOnTheFifteenth) };
        var settings = new PluginConfiguration { RollupTimeZone = "Europe/Berlin" };

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) => YearInReview.Over(plays, userId, year, zone, topCount, null),
            configuration: settings);

        var who = Caller.Someone;

        Assert.Equal("2025-06-16", BusiestDayOf(await endpoints.Get(Path(who.UserId, 2025), who)));

        settings.RollupTimeZone = "America/New_York";

        Assert.Equal("2025-06-15", BusiestDayOf(await endpoints.Get(Path(who.UserId, 2025), who)));
    }

    /// <summary>
    /// The answer carries the zone its days were read in, so a figure and the
    /// boundary that produced it cannot come apart on the way to a page. This
    /// is what a view refuses to draw without, and it is why nothing at the far
    /// end has to work a boundary out for itself.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerSaysWhichZoneItsDaysWereReadIn()
    {
        var settings = new PluginConfiguration { RollupTimeZone = "America/New_York" };

        using var endpoints = new InProcessEndpoints(configuration: settings);

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(200, answer.Status);
        Assert.Equal("America/New_York", ZoneOf(answer));
    }

    private static string? ZoneOf(InProcessEndpoints.Answer answer)
    {
        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        return body.RootElement.GetProperty("zoneId").GetString();
    }

    private static string? BusiestDayOf(InProcessEndpoints.Answer answer)
    {
        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        return body.RootElement.GetProperty("busiestDay").GetProperty("day").GetString();
    }

    private static string Path(Guid userId, int year)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/Years/{1}",
            userId.ToString("D", CultureInfo.InvariantCulture),
            year.ToString(CultureInfo.InvariantCulture));

    private static PlayRecord APlay(DateTime startedUtc) => new()
    {
        SchemaVersion = 1,
        UserId = Caller.Someone.UserId,
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Movie",
        ParentId = null,
        ItemName = "A film",
        ItemRuntime = TimeSpan.FromMinutes(100),
        StartedUtc = startedUtc,
        EndedUtc = startedUtc.AddMinutes(100),
        WatchedDuration = TimeSpan.FromMinutes(100),
        ReachedTheEnd = true,
        ClientName = "Jellyfin Web",
        DeviceId = "device-1",
        DeviceName = "A browser",
        PlayMethodAtStart = PlayMethod.DirectPlay,
        PlayMethodChangedUtc = null,
        Transcode = new TranscodeSummary
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            VideoWasDirect = true,
            AudioWasDirect = true,
            PeakBitrate = null,
            TypicalBitrate = null,
            HardwareAcceleration = null,
            Reasons = Array.Empty<string>()
        }
    };
}
