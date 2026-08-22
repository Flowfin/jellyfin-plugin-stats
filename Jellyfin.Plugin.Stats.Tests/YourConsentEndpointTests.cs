// The route by which one account says whether it may be named, driven over the
// in-process route.
//
// The matrix beside this file says who gets which status, and its two rows about
// somebody else's answer are the first condition of issue #42. This says what
// the endpoint does with the request once it has decided that.

using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class YourConsentEndpointTests
{
    private static readonly DateTimeOffset March = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// An account that has never answered is told so, and told what it is being
    /// asked. A page that had to fetch the wording separately could show one
    /// version's words beside another version's number.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAccountThatHasNotAnsweredIsToldSoAndToldTheQuestion()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(March));

        var who = Caller.Someone;
        var answer = await endpoints.Send("GET", Path(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.False(body.RootElement.GetProperty("answered").GetBoolean());
        Assert.False(body.RootElement.GetProperty("agreed").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("agreedToVersion").GetInt32());
        Assert.Equal(ConsentWording.Version, body.RootElement.GetProperty("currentVersion").GetInt32());
        Assert.Equal(ConsentWording.Text, body.RootElement.GetProperty("wording").GetString());
    }

    /// <summary>
    /// An account agrees, and reads back what it said on the next request.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAccountAgreesAndReadsBackWhatItSaid()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(March));

        var who = Caller.Someone;

        var recorded = await endpoints.Send("PUT", Path(who.UserId), who, Agreeing(ConsentWording.Version));

        Assert.Equal(200, recorded.Status);

        var read = await endpoints.Send("GET", Path(who.UserId), who);

        using var body = JsonDocument.Parse(read.Body);

        Assert.True(body.RootElement.GetProperty("answered").GetBoolean());
        Assert.True(body.RootElement.GetProperty("agreed").GetBoolean());
        Assert.Equal(ConsentWording.Version, body.RootElement.GetProperty("agreedToVersion").GetInt32());
    }

    /// <summary>
    /// Withdrawing takes effect for the next request and keeps the agreement it
    /// withdraws beside it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WithdrawingTakesEffectAndKeepsTheAgreementItWithdraws()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(March));

        var who = Caller.Someone;

        await endpoints.Send("PUT", Path(who.UserId), who, Agreeing(ConsentWording.Version));
        await endpoints.Send("PUT", Path(who.UserId), who, "{\"Agreed\":false}");

        var read = await endpoints.Send("GET", Path(who.UserId), who);

        using var body = JsonDocument.Parse(read.Body);

        Assert.True(body.RootElement.GetProperty("answered").GetBoolean());
        Assert.False(body.RootElement.GetProperty("agreed").GetBoolean());
        Assert.NotNull(body.RootElement.GetProperty("agreedUtc").GetString());
        Assert.NotNull(body.RootElement.GetProperty("withdrawnUtc").GetString());
    }

    /// <summary>
    /// The third condition of issue #42, at the endpoint. An agreement naming a
    /// version this build does not ship is refused, and nothing is recorded, so
    /// the account is asked again rather than being taken to have agreed to
    /// something it never read.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AnAgreementToAnotherVersionIsRefusedAndNothingIsRecorded()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(March));

        var who = Caller.Someone;

        var refused = await endpoints.Send(
            "PUT",
            Path(who.UserId),
            who,
            Agreeing(ConsentWording.Version + 1));

        Assert.Equal(400, refused.Status);

        var read = await endpoints.Send("GET", Path(who.UserId), who);

        using var body = JsonDocument.Parse(read.Body);

        Assert.False(body.RootElement.GetProperty("answered").GetBoolean());
    }

    /// <summary>
    /// A request carrying no body at all is refused before the action is
    /// reached, so the action holds no check for one. What it comes back as is
    /// asserted rather than assumed: a request with no body carries no content
    /// type either, and what the framework says to that is that it cannot read
    /// the media type rather than that the request was bad.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ARequestWithNoBodyIsRefusedBeforeTheActionIsReached()
    {
        using var endpoints = new InProcessEndpoints(clock: new FixedClock(March));

        var who = Caller.Someone;
        var answer = await endpoints.Send("PUT", Path(who.UserId), who);

        Assert.Equal(415, answer.Status);

        // And nothing was recorded by it, which is the half that matters.
        var read = await endpoints.Send("GET", Path(who.UserId), who);

        using var body = JsonDocument.Parse(read.Body);

        Assert.False(body.RootElement.GetProperty("answered").GetBoolean());
    }

    /// <summary>
    /// A store that will not open is a status rather than an answer saying
    /// nobody has agreed. Those are opposite facts about what somebody said.
    /// </summary>
    /// <param name="method">Which request.</param>
    /// <param name="body">Its body, where it carries one.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("GET", null)]
    [InlineData("PUT", "{\"Agreed\":false}")]
    public async Task AStoreThatCannotBeOpenedIsNotAnAnswer(string method, string? body)
    {
        using var endpoints = new InProcessEndpoints(
            clock: new FixedClock(March),
            consent: new ConsentRegister(
                () => throw new IOException("The store is not there."),
                new FixedClock(March)));

        var who = Caller.Someone;
        var answer = await endpoints.Send(method, Path(who.UserId), who, body);

        Assert.Equal(503, answer.Status);
    }

    /// <summary>
    /// The endpoint refuses to be built without the two things it cannot work
    /// without, rather than failing on the first request that reaches it.
    /// </summary>
    [Fact]
    public void TheEndpointRefusesToBeBuiltOnNothing()
    {
        var register = new ConsentRegister(() => throw new IOException("Nothing is opened."), new FixedClock(March));

        Assert.Throws<ArgumentNullException>(
            () => new Jellyfin.Plugin.Stats.Api.YourConsentController(null!, null!));
        Assert.Throws<ArgumentNullException>(
            () => new Jellyfin.Plugin.Stats.Api.YourConsentController(register, null!));
    }

    private static string Agreeing(int version)
        => string.Format(CultureInfo.InvariantCulture, "{{\"Agreed\":true,\"WordingVersion\":{0}}}", version);

    private static string Path(Guid userId)
        => "/Stats/Users/" + userId.ToString("D", CultureInfo.InvariantCulture) + "/Consent";
}
