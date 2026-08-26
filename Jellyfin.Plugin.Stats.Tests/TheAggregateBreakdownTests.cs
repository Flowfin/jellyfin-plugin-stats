// What a range divides into, driven over the in-process route.
//
// Issue #59 asks for a breakdown by client and by device over a range, that it
// identifies clients and devices and no user, that a client one account is
// behind does not become a way to read that account, and that a value the server
// reported nothing for is shown as such and counted rather than dropped. The
// fold and the drawing already hold those; what these drive is the request
// between them, so each condition is asserted against a response an
// administrator actually receives rather than against the arithmetic under it.
//
// Who may ask is the authorization matrix's, and the four caller shapes are
// crossed with this route there rather than here.

using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the aggregate breakdown answers, and what it refuses.
/// </summary>
public class TheAggregateBreakdownTests
{
    private const string AWeekInMarch = "/Stats/Reports/Breakdown?from=2026-03-14T00:00:00Z&to=2026-03-21T00:00:00Z";

    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Gets a dimension no request may name.
    /// </summary>
    /// <remarks>
    /// The last three are the ones this route exists against rather than typing
    /// mistakes. A request cannot ask to be shown people by writing the word,
    /// and it cannot ask by writing the number of a member either, because the
    /// choice never arrives as the enumeration.
    /// </remarks>
    public static TheoryData<string> DimensionsInNoSet =>
    [
        "banana",
        string.Empty,
        "user",
        "userId",
        "0",
        "1"
    ];

    /// <summary>
    /// A range every member of which two accounts stand behind is answered as
    /// rows, with nothing folded.
    /// </summary>
    /// <param name="query">Which dimension the request names.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("&dimension=client")]
    [InlineData("&dimension=CLIENT")]
    [InlineData("&dimension=device")]
    public async Task ARangeEveryMemberOfWhichIsShowableIsAnsweredAsRows(string query)
    {
        using var endpoints = Over(
            APlay(Alice, "Jellyfin Web", "device-1", March),
            APlay(Bob, "Jellyfin Web", "device-1", March.AddHours(1)));

        var answer = await endpoints.Get(AWeekInMarch + query, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.False(body.RootElement.GetProperty("withheld").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("combined").ValueKind);
        Assert.Equal(2, body.RootElement.GetProperty("plays").GetInt64());
    }

    /// <summary>
    /// A member one account alone used is not a row, and its plays are in the
    /// group beside the rows rather than gone.
    /// </summary>
    /// <remarks>
    /// The second condition of issue #59, asserted at the response. A client
    /// with a single account behind it does not become a way to read that
    /// account, and the way it does not is that it is never a row: the rule is
    /// issue #41's and this is what a caller sees of it.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AMemberOneAccountUsedIsInTheGroupAndNotARow()
    {
        using var endpoints = Over(
            APlay(Alice, "Jellyfin Web", "device-1", March),
            APlay(Bob, "Jellyfin Web", "device-1", March.AddHours(1)),
            APlay(Alice, "Roku", "device-2", March.AddHours(2)),
            APlay(Bob, "Kodi", "device-3", March.AddHours(3)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(1, body.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal("Jellyfin Web", body.RootElement.GetProperty("rows")[0].GetProperty("key").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("combined").GetProperty("plays").GetInt64());
        Assert.Equal(4, body.RootElement.GetProperty("plays").GetInt64());

        // Neither the group nor anything beside it says which members went into
        // it. A response naming them would put the rule back where it started.
        Assert.DoesNotContain("Roku", answer.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Kodi", answer.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A breakdown whose group would stand on one account is withheld, and
    /// withheld is not empty.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ABreakdownThatMayNotBeShownIsWithheldRatherThanEmptied()
    {
        using var endpoints = Over(
            APlay(Alice, "Jellyfin Web", "device-1", March),
            APlay(Bob, "Jellyfin Web", "device-1", March.AddHours(1)),
            APlay(Bob, "Roku", "device-2", March.AddHours(2)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.True(body.RootElement.GetProperty("withheld").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("rows").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("combined").ValueKind);

        // No play count either. A withheld breakdown that reported how many
        // plays it was not showing would be a place to read a figure from, and
        // the total over the same range is a question with an answer of its own.
        Assert.Equal(0, body.RootElement.GetProperty("plays").GetInt64());
    }

    /// <summary>
    /// A member the server named nothing for is a row and is counted, rather
    /// than being dropped out of the answer.
    /// </summary>
    /// <remarks>
    /// The third condition of issue #59 at the response. The row carries no
    /// name rather than an invented one, so a client genuinely called Unknown
    /// stays a different row from the plays nobody could attribute, and whoever
    /// draws the picture decides the wording.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AMemberTheServerNamedNothingForIsARowAndIsCounted()
    {
        using var endpoints = Over(
            APlay(Alice, "Jellyfin Web", "device-1", March),
            APlay(Bob, "Jellyfin Web", "device-1", March.AddHours(1)),
            APlay(Alice, string.Empty, "device-1", March.AddHours(2)),
            APlay(Bob, string.Empty, "device-1", March.AddHours(3)));

        var answer = await endpoints.Get(AWeekInMarch + "&dimension=client", Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        var rows = body.RootElement.GetProperty("rows");

        Assert.Equal(2, rows.GetArrayLength());
        Assert.Equal(4, body.RootElement.GetProperty("plays").GetInt64());

        // The row carries no name rather than the word, so a client genuinely
        // called Unknown would still be a row of its own and whoever draws the
        // picture chooses the wording. Two plays are under it and they are in
        // the answer, which is what counted rather than dropped comes to.
        var nameless = rows.EnumerateArray().Single(row => row.GetProperty("name").ValueKind == JsonValueKind.Null);

        Assert.Equal(2, nameless.GetProperty("delivery").GetProperty("plays").GetInt64());
    }

    /// <summary>
    /// Nothing in the answer names an account, whatever the plays were folded
    /// from.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerNamesNoAccount()
    {
        using var endpoints = Over(
            APlay(Alice, "Jellyfin Web", "device-1", March),
            APlay(Bob, "Jellyfin Web", "device-1", March.AddHours(1)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);
        Assert.DoesNotContain(Alice.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Alice.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A dimension that is not a member of the set is refused.
    /// </summary>
    /// <param name="asked">What the request names.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [MemberData(nameof(DimensionsInNoSet))]
    public async Task ADimensionInNoSetIsRefused(string asked)
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new InvalidOperationException("Nothing should be opened.")));

        var answer = await endpoints.Get(
            AWeekInMarch + "&dimension=" + Uri.EscapeDataString(asked),
            Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A range this plugin will not answer over is refused.
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

        var answer = await endpoints.Get("/Stats/Reports/Breakdown" + query, Caller.Administrator);

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
                [APlay(Alice, "Jellyfin Web", "device-1", March)],
                asManyAsAnyBoundAsksFor: true)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is answered as the plugin being unavailable
    /// rather than as a range nobody watched anything in.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAnEmptyBreakdown()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new StoreCouldNotBeOpenedException()));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(503, answer.Status);
    }

    private static InProcessEndpoints Over(params PlayRecord[] plays)
        => new(reports: new AggregateQueries(() => new PlaysHeldInMemory(plays)));

    private static PlayRecord APlay(Guid userId, string client, string deviceId, DateTime startedUtc)
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
            ClientName = client,
            DeviceId = deviceId,
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
