// The first report this plugin serves that is not about the account asking,
// driven over the in-process route.
//
// Issue #55's second condition is what most of this file is: every filter and
// sort parameter maps through a closed set, and an unknown value is refused
// rather than passed through. A condition about how parameters are mapped needs
// parameters, so the cases below drive real ones at a real action and read the
// status rather than asserting about the shape of the source.
//
// Who may ask was decided on that issue on 2026-08-24 and is an administrator
// only. The four caller shapes are crossed with this route in the authorization
// matrix; what is here is what that table cannot say, which is what happens to
// a request an administrator makes badly.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the aggregate top list answers, and what it refuses.
/// </summary>
public class TheAggregateTopListTests
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly Guid AnotherFilm = Guid.Parse("22222222-3333-4444-5555-666666666666");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Gets the spellings of a choice that no request may use.
    /// </summary>
    /// <remarks>
    /// Each is a way of naming something that is not a member of the set. The
    /// last four are the shapes this issue exists against rather than typing
    /// mistakes: a column name, an order-by fragment, and the numbers the
    /// framework converts into members when the parameter is declared as the
    /// enumeration itself. Nought and one are the two members' own numbers and
    /// are the pair that leaks on that shape; five is outside the members and
    /// binding refuses it, so it is here to record which half of the vocabulary
    /// the framework was already holding.
    /// </remarks>
    public static TheoryData<string> ValuesInNeitherSet =>
    [
        "banana",
        string.Empty,
        "watched time",
        "Item;--",
        "UserId",
        "Plays%20DESC",
        "0",
        "1",
        "5"
    ];

    /// <summary>
    /// An administrator asking over a range gets the list.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAdministratorAskingOverARangeIsAnswered()
    {
        using var endpoints = Over(
            APlay(Alice, AFilm, March),
            APlay(Bob, AFilm, March.AddHours(2)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.False(body.RootElement.GetProperty("withheld").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("rows").GetArrayLength());
    }

    /// <summary>
    /// A list standing on one account is withheld, and withheld is not empty.
    /// </summary>
    /// <remarks>
    /// The distinction the response type exists for. A server where one person
    /// watched one thing and a server where nobody watched anything are
    /// different facts, and a page told the second when the first is true would
    /// report an idle server to its administrator.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AListStandingOnOneAccountIsWithheldRatherThanEmptied()
    {
        using var endpoints = Over(APlay(Alice, AFilm, March));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.True(body.RootElement.GetProperty("withheld").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("rows").GetArrayLength());
    }

    /// <summary>
    /// Nothing in the answer names an account, whatever the rows were folded
    /// from.
    /// </summary>
    /// <remarks>
    /// Issue #41's first condition, asserted against this endpoint's response
    /// rather than against the fold under it. A shape that cannot name an
    /// account and a response that does not carry one are different statements,
    /// and only the second is what a caller receives. The accounts behind the
    /// plays are looked for by their own identifiers, so a field added later
    /// that carried one would redden this without anybody naming the field.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerNamesNoAccount()
    {
        using var endpoints = Over(
            APlay(Alice, AFilm, March),
            APlay(Bob, AFilm, March.AddHours(2)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(200, answer.Status);
        Assert.DoesNotContain(Alice.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("D", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Alice.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("N", CultureInfo.InvariantCulture), answer.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every spelling the two sets admit is answered.
    /// </summary>
    /// <remarks>
    /// The near miss for the refusals below. A guard that refused every value
    /// would pass every case in this file except this one, and a closed set
    /// nothing may pass through is a parameter nobody may use.
    /// </remarks>
    /// <param name="query">The choices on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("&grouping=item")]
    [InlineData("&grouping=Item")]
    [InlineData("&grouping=ITEM")]
    [InlineData("&grouping=series")]
    [InlineData("&order=watchedTime")]
    [InlineData("&order=watchedtime")]
    [InlineData("&order=plays")]
    [InlineData("&grouping=item&order=plays")]
    [InlineData("")]
    public async Task AChoiceThisPluginKnowsIsAnswered(string query)
    {
        using var endpoints = Over(
            APlay(Alice, AFilm, March),
            APlay(Bob, AFilm, March.AddHours(2)));

        var answer = await endpoints.Get(AWeekInMarch + query, Caller.Administrator);

        Assert.Equal(200, answer.Status);
    }

    /// <summary>
    /// A grouping that is not a member of the set is refused.
    /// </summary>
    /// <param name="asked">What the request names.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [MemberData(nameof(ValuesInNeitherSet))]
    public async Task AGroupingInNoSetIsRefused(string asked)
    {
        using var endpoints = Over(NothingWasWatched);

        var answer = await endpoints.Get(AWeekInMarch + "&grouping=" + Uri.EscapeDataString(asked), Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// An order that is not a member of the set is refused.
    /// </summary>
    /// <param name="asked">What the request names.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [MemberData(nameof(ValuesInNeitherSet))]
    public async Task AnOrderInNoSetIsRefused(string asked)
    {
        using var endpoints = Over(NothingWasWatched);

        var answer = await endpoints.Get(AWeekInMarch + "&order=" + Uri.EscapeDataString(asked), Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A choice this plugin does not know is refused before the store is
    /// opened.
    /// </summary>
    /// <remarks>
    /// The refusal above is worth less if it happens after the read. A guard
    /// that fires only once the rows are in hand is a guard whose verdict
    /// depends on what the range held, and the same request would be refused
    /// over one server and answered over another.
    /// </remarks>
    /// <param name="query">The choice on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("&grouping=banana")]
    [InlineData("&order=banana")]
    public async Task AChoiceInNoSetIsRefusedBeforeAnythingIsOpened(string query)
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new InvalidOperationException("Nothing should be opened.")));

        var answer = await endpoints.Get(AWeekInMarch + query, Caller.Administrator);

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
        using var endpoints = Over(NothingWasWatched);

        var answer = await endpoints.Get("/Stats/Reports/Top" + query, Caller.Administrator);

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
            reports: new AggregateQueries(() => new PlaysHeldInMemory([APlay(Alice, AFilm, March)], asManyAsAnyBoundAsksFor: true)));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is answered as the plugin being unavailable
    /// rather than as a server nobody watched anything on.
    /// </summary>
    /// <remarks>
    /// The same statement issue #31 asks of every endpoint here, made at the
    /// first one whose answer is about the server. An empty top list and a
    /// plugin with no answer would otherwise reach an administrator as the same
    /// page.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAnEmptyList()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new StoreCouldNotBeOpenedException()));

        var answer = await endpoints.Get(AWeekInMarch, Caller.Administrator);

        Assert.Equal(503, answer.Status);
    }

    /// <summary>
    /// A parameter the action never declared changes nothing about the answer.
    /// </summary>
    /// <remarks>
    /// The shape issue #55 is named for, arriving beside two declared choices
    /// rather than instead of them. A caller who cannot get a column list past
    /// the mapping tries adding one, and the action takes what it declares and
    /// nothing else.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AParameterTheActionNeverDeclaredChangesNothing()
    {
        using var endpoints = Over(
            APlay(Alice, AFilm, March),
            APlay(Bob, AFilm, March.AddHours(2)));

        var plain = await endpoints.Get(AWeekInMarch, Caller.Administrator);
        var loaded = await endpoints.Get(
            AWeekInMarch + "&columns=UserId&orderBy=Watched%20DESC&limit=1000&sort=UserId",
            Caller.Administrator);

        Assert.Equal(200, loaded.Status);
        Assert.Equal(plain.Body, loaded.Body);
    }

    /// <summary>
    /// The order decides which rows survive the cut, so the two members of that
    /// set are answers rather than spellings that reach the same thing.
    /// </summary>
    /// <remarks>
    /// A mapping that mapped every spelling to one member would pass every
    /// other case here. Two items, one watched longer and the other watched
    /// more often, and the two orders put a different one first.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheTwoOrdersDisagreeAboutWhichTitleIsFirst()
    {
        using var endpoints = Over(
            APlay(Alice, AFilm, March, TimeSpan.FromMinutes(200)),
            APlay(Bob, AFilm, March.AddHours(6), TimeSpan.FromMinutes(200)),
            APlay(Alice, AnotherFilm, March.AddDays(1), TimeSpan.FromMinutes(10)),
            APlay(Bob, AnotherFilm, March.AddDays(1).AddHours(1), TimeSpan.FromMinutes(10)),
            APlay(Alice, AnotherFilm, March.AddDays(2), TimeSpan.FromMinutes(10)),
            APlay(Bob, AnotherFilm, March.AddDays(2).AddHours(1), TimeSpan.FromMinutes(10)));

        var byTime = await endpoints.Get(AWeekInMarch + "&order=watchedTime", Caller.Administrator);
        var byPlays = await endpoints.Get(AWeekInMarch + "&order=plays", Caller.Administrator);

        Assert.Equal(AFilm, FirstKey(byTime.Body));
        Assert.Equal(AnotherFilm, FirstKey(byPlays.Body));
    }

    private const string AWeekInMarch = "/Stats/Reports/Top?from=2026-03-14T00:00:00Z&to=2026-03-21T00:00:00Z";

    private static readonly PlayRecord[] NothingWasWatched = [];

    private static InProcessEndpoints Over(params PlayRecord[] plays)
        => new(reports: new AggregateQueries(() => new PlaysHeldInMemory(plays)));

    private static Guid FirstKey(string body)
    {
        using var read = JsonDocument.Parse(body);

        return read.RootElement.GetProperty("rows")[0].GetProperty("key").GetGuid();
    }

    private static PlayRecord APlay(Guid userId, Guid itemId, DateTime startedUtc, TimeSpan? watched = null)
    {
        var length = watched ?? TimeSpan.FromMinutes(40);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Movie",
            ParentId = null,
            ItemName = itemId == AFilm ? "A Film" : "Another Film",
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
