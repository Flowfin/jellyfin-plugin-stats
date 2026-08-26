// How much the server was used over a range, driven over the in-process route.
//
// Issue #57 asks for a view of plays and watched time per day with the direct
// and transcoded split in the same picture, rendered from one request and
// naming no user. The drawing exists and the fold exists; what these drive is
// the request between them, so the two conditions that are statements about the
// request are asserted against what an administrator receives rather than
// against the arithmetic under it.
//
// Who may ask is the authorization matrix's, and the four caller shapes are
// crossed with this route there rather than here.

using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the aggregate usage series answers, and what it refuses.
/// </summary>
public class TheAggregateUsageTests
{
    private const string AWeekInMarch = "/Stats/Reports/Usage?from=2026-03-14T00:00:00Z&to=2026-03-21T00:00:00Z";

    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// One request answers every day the range covers, so a page drawing it
    /// fetches once.
    /// </summary>
    /// <remarks>
    /// The first condition of issue #57 as a property of the action. Each row
    /// carries the day, the watched time and the delivery split, which is what
    /// stops the view being three requests, and the range's own totals are
    /// beside them rather than left to be added up.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task OneRequestAnswersEveryDayTheRangeCovers()
    {
        using var endpoints = Over(
            APlay(Alice, March),
            APlay(Bob, March.AddHours(2)),
            APlay(Alice, March.AddDays(2)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(3, body.RootElement.GetProperty("plays").GetInt64());

        var rows = body.RootElement.GetProperty("rows");

        Assert.True(rows.GetArrayLength() >= 2);

        var first = rows[0];

        Assert.Equal("2026-03-14", first.GetProperty("day").GetString());
        Assert.Equal(2, first.GetProperty("delivery").GetProperty("plays").GetInt64());
        Assert.True(first.TryGetProperty("watched", out _));
    }

    /// <summary>
    /// The answer says which zone the days were read in, and it is the zone the
    /// settings name rather than anything the request said.
    /// </summary>
    /// <remarks>
    /// A page that stated a zone it was not given would be quoting a setting
    /// rather than saying anything about the numbers it drew. The request
    /// carries no zone at all, which is what
    /// <c>no-time-offset-from-the-request</c> refuses the other spelling of.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerCarriesTheZoneTheSettingsName()
    {
        var settings = new PluginConfiguration { RollupTimeZone = "UTC" };

        using var endpoints = new InProcessEndpoints(
            configuration: settings,
            reports: new AggregateQueries(() => new PlaysHeldInMemory([APlay(Alice, March)])));

        var answer = await endpoints.Get(AWeekInMarch + "&zone=Asia/Tokyo&utcOffset=540", Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(settings.RollupTimeZone, body.RootElement.GetProperty("zoneId").GetString());
    }

    /// <summary>
    /// Nothing in the answer names an account.
    /// </summary>
    /// <remarks>
    /// The third condition of issue #57, asserted against the response rather
    /// than by reading the page. A page that happens not to display a field it
    /// was sent is not a page that was not sent it.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerNamesNoAccount()
    {
        using var endpoints = Over(APlay(Alice, March), APlay(Bob, March.AddHours(2)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);
        Assert.DoesNotContain(Alice.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Alice.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A range this plugin will not answer over is refused, which is the cap a
    /// page has to state rather than discover.
    /// </summary>
    /// <param name="query">The range on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("?from=2026-03-14T00:00:00Z")]
    [InlineData("?to=2026-03-21T00:00:00Z")]
    [InlineData("")]
    [InlineData("?from=&to=")]
    [InlineData("?from=banana&to=banana")]
    [InlineData("?from=2026-03-21T00:00:00Z&to=2026-03-14T00:00:00Z")]
    [InlineData("?from=2024-01-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    public async Task ARangeThisPluginWillNotAnswerOverIsRefused(string query)
    {
        using var endpoints = Over();

        var answer = await endpoints.Get("/Stats/Reports/Usage" + query, Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A range holding more plays than the plugin will read is refused rather
    /// than answered from the part of it that fitted.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARangeHoldingMorePlaysThanTheBoundIsRefused()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => new PlaysHeldInMemory(
                [APlay(Alice, March)],
                asManyAsAnyBoundAsksFor: true)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is answered as the plugin being unavailable
    /// rather than as a week nobody watched anything in.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAQuietWeek()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new StoreCouldNotBeOpenedException()));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(503, answer.Status);
    }

    private static InProcessEndpoints Over(params PlayRecord[] plays)
        => new(reports: new AggregateQueries(() => new PlaysHeldInMemory(plays)));

    private static PlayRecord APlay(Guid userId, DateTime startedUtc)
    {
        var length = TimeSpan.FromMinutes(40);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = AFilm,
            ItemType = "Movie",
            ParentId = null,
            ItemName = "A Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc + length,
            WatchedDuration = length,
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = []
            }
        };
    }
}
