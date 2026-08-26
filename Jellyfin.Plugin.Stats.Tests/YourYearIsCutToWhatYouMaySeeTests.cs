// What the year endpoint serves once the library has been asked. Issue #54.
//
// The fold and the cut are proved next door, over objects. This is the half
// that says the endpoint actually asks: the route between a held year and a
// response is where a filter is easiest to leave out, because leaving it out
// changes nothing anybody can see without a library that withholds something.
//
// Every case here drives a request through the in-process pipeline, so what is
// asserted is the body a caller receives rather than the object a method
// returned.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class YourYearIsCutToWhatYouMaySeeTests
{
    private static readonly Guid Withheld = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000000a");

    private static readonly Guid Deleted = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000000b");

    private static readonly Guid Shown = Guid.Parse("a1b2c3d4-0000-0000-0000-00000000000c");

    /// <summary>
    /// An item this account may not see is not named in the response, an item
    /// the library has let go of is, and every total counts all three. This is
    /// the endpoint half of both conditions of issue #54.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheResponseNamesWhatTheCallerMaySeeAndCountsWhatTheyDidNot()
    {
        var who = Caller.Someone;

        var library = FakeItemAccess.EverythingVisible
            .Withholding(who.UserId, Withheld)
            .LetGoOf(Deleted);

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) => YearInReview.Over(
                ThreePlaysBy(userId),
                userId,
                year,
                zone,
                topCount,
                oldestPlayStartedUtc: null),
            access: library);

        var answer = await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);
        var root = body.RootElement;

        var named = root.GetProperty("topItems")
            .EnumerateArray()
            .Select(row => row.GetProperty("name").GetString())
            .ToList();

        Assert.Equal(new[] { "Deleted since", "Still shown" }, named);
        Assert.DoesNotContain("Withheld since", named);

        // Every play is still counted. The name is the only thing access
        // decides, which is the sentence this issue is written on.
        Assert.Equal(3, root.GetProperty("plays").GetInt64());
        Assert.Equal(3, root.GetProperty("distinctItems").GetInt64());
    }

    /// <summary>
    /// The same held year read by two accounts is two different lists. The fold
    /// runs once, which is what asserts that the cut is taken on the way out
    /// rather than being folded in and kept.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OneFoldedYearIsCutTwiceForTwoAccounts()
    {
        var who = Caller.Someone;
        var folds = new List<Guid>();

        var library = FakeItemAccess.EverythingVisible.Withholding(who.UserId, Withheld);

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                folds.Add(userId);
                return YearInReview.Over(
                    ThreePlaysBy(userId),
                    userId,
                    year,
                    zone,
                    topCount,
                    oldestPlayStartedUtc: null);
            },
            access: library);

        var first = await endpoints.Get(Path(who.UserId, 2025), who);
        var second = await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(200, first.Status);
        Assert.Equal(200, second.Status);

        // A finished year is held after the first reading, so the second
        // response is cut from the answer the first one was cut from.
        Assert.Single(folds);
        Assert.Equal(first.Body, second.Body);

        using var body = JsonDocument.Parse(second.Body);
        Assert.DoesNotContain(
            body.RootElement.GetProperty("topItems").EnumerateArray(),
            row => row.GetProperty("name").GetString() == "Withheld since");
    }

    /// <summary>
    /// The registration, so that what serves a request on a server is the
    /// library-backed answer rather than nothing at all.
    /// </summary>
    /// <remarks>
    /// What is proved here is the registration and not the library behind it,
    /// the same way the channel names are: the server's library and its accounts
    /// are asked for when an item is, so nothing in this process resolves them.
    /// </remarks>
    [Fact]
    public void WhatDecidesAccessIsResolvedFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionManager>(new FakeSessionManager());
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        Assert.IsType<LibraryItemAccess>(provider.GetRequiredService<IItemAccess>());
    }

    private static string Path(Guid userId, int year)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/Years/{1}",
            userId.ToString("D", CultureInfo.InvariantCulture),
            year.ToString(CultureInfo.InvariantCulture));

    private static List<PlayRecord> ThreePlaysBy(Guid userId) =>
    [
        APlay(userId, Withheld, "Withheld since", TimeSpan.FromMinutes(90)),
        APlay(userId, Deleted, "Deleted since", TimeSpan.FromMinutes(60)),
        APlay(userId, Shown, "Still shown", TimeSpan.FromMinutes(30))
    ];

    private static PlayRecord APlay(Guid userId, Guid itemId, string itemName, TimeSpan watched)
    {
        var started = new DateTime(2025, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        return new PlayRecord
        {
            SchemaVersion = 1,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Movie",
            ParentId = null,
            ItemName = itemName,
            ItemRuntime = TimeSpan.FromMinutes(120),
            ChannelName = null,
            StartedUtc = started,
            EndedUtc = started.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
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
}
