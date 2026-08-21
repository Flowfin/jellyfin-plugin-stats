// The first endpoint of this plugin, driven over the in-process route.
//
// The matrix beside this file says who gets which status. This says what the
// endpoint does with the request once it has decided that, and it drives the
// identity check directly as well as through a request, because issue #43 asks
// that removing that check fail a test rather than change a response body.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Net;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the year endpoint answers, and what it refuses.
/// </summary>
public class YourYearEndpointTests
{
    private static readonly DateTimeOffset MidJune2026 = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The caller gets their own year, and the fold is asked for their account
    /// rather than for the one in the route.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// Both are the same identifier on a correct request, which is why the
    /// asked-for account is recorded and compared rather than the response
    /// being read for a name. An endpoint that passed the route value straight
    /// through would pass a test that only looked at the body.
    /// </remarks>
    [Fact]
    public async Task ACallerReadsTheirOwnYear()
    {
        var asked = new List<Guid>();

        using var endpoints = new InProcessEndpoints(fold: (userId, year, zone, topCount) =>
        {
            asked.Add(userId);
            return YearInReview.Over([], userId, year, zone, topCount, null);
        });

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(200, answer.Status);
        Assert.Equal(who.UserId, Assert.Single(asked));
    }

    /// <summary>
    /// The answer is the year that was asked for, read in the zone the settings
    /// name.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheAnswerCarriesTheYearAndTheZoneItWasReadIn()
    {
        var settings = new PluginConfiguration { RollupTimeZone = "Pacific/Auckland" };

        using var endpoints = new InProcessEndpoints(configuration: settings);

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);
        Assert.Equal(2025, body.RootElement.GetProperty("year").GetInt32());
        Assert.Equal("Pacific/Auckland", body.RootElement.GetProperty("zoneId").GetString());
    }

    /// <summary>
    /// The top list bound the endpoint hands the fold is the constant, and not
    /// the response bound out of the settings.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The two are different numbers for a reason, and reading one as the other
    /// is how a top ten becomes a list of everything the account ever watched.
    /// The settings here carry a response bound that is nothing like the
    /// constant, so a test that passed by coincidence cannot.
    /// </remarks>
    [Fact]
    public async Task TheTopListBoundIsTheConstantAndNotASetting()
    {
        var settings = new PluginConfiguration { MaximumRowsPerResponse = 500 };
        var bounds = new List<int>();

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                bounds.Add(topCount);
                return YearInReview.Over([], userId, year, zone, topCount, null);
            },
            configuration: settings);

        var who = Caller.Someone;
        await endpoints.Get(Path(who.UserId, 2025), who);

        Assert.Equal(YourYearController.TopListLength, Assert.Single(bounds));
    }

    /// <summary>
    /// A year that has not happened is refused rather than folded.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AYearTheServerHasNotReachedIsNotAnswered()
    {
        var folds = 0;

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                folds++;
                return YearInReview.Over([], userId, year, zone, topCount, null);
            },
            clock: new FixedClock(MidJune2026));

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, 2027), who);

        Assert.Equal(404, answer.Status);
        Assert.Equal(0, folds);
    }

    /// <summary>
    /// The year the server is in is answered, so the bound refuses what is
    /// after it and not what is in it.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// The neighbouring test would pass against a bound that was one year too
    /// tight, and the year somebody opens a wrap-up page for first is the one
    /// they are living in.
    /// </remarks>
    [Fact]
    public async Task TheYearTheServerIsInIsAnswered()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(MidJune2026));

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, 2026), who);

        Assert.Equal(200, answer.Status);
    }

    /// <summary>
    /// A year below the floor is refused rather than folded and held.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AYearBelowTheFloorIsNotAnswered()
    {
        var folds = 0;

        using var endpoints = new InProcessEndpoints(fold: (userId, year, zone, topCount) =>
        {
            folds++;
            return YearInReview.Over([], userId, year, zone, topCount, null);
        });

        var who = Caller.Someone;
        var answer = await endpoints.Get(Path(who.UserId, YourYearController.EarliestYearAnswered - 1), who);

        Assert.Equal(404, answer.Status);
        Assert.Equal(0, folds);
    }

    /// <summary>
    /// The identity check, driven directly rather than through a request.
    /// </summary>
    /// <param name="asked">The account named in the route.</param>
    /// <param name="caller">The account that made the request.</param>
    /// <param name="expected">Whether those are the same account.</param>
    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", true)]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", false)]
    [InlineData("00000000-0000-0000-0000-000000000000", "00000000-0000-0000-0000-000000000000", false)]
    public void TheIdentityCheckAnswersForOneAccountOnly(string asked, string caller, bool expected)
    {
        var account = new Guid(caller);

        var made = new AuthorizationInfo
        {
            User = account.Equals(Guid.Empty) ? null : FakeUserManager.NewUser("whoever", account),
            IsAuthenticated = !account.Equals(Guid.Empty)
        };

        Assert.Equal(expected, CallerIdentity.AsksForTheirOwnRows(new Guid(asked), made));
    }

    /// <summary>
    /// The identity check refuses a caller it was handed nothing about, rather
    /// than treating the absence as a match.
    /// </summary>
    [Fact]
    public void TheIdentityCheckRefusesToJudgeACallerItWasNotGiven()
        => Assert.Throws<ArgumentNullException>(
            () => CallerIdentity.AsksForTheirOwnRows(Caller.Someone.UserId, null!));

    private static string Path(Guid userId, int year)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/Years/{1}",
            userId.ToString("D", CultureInfo.InvariantCulture),
            year.ToString(CultureInfo.InvariantCulture));
}
