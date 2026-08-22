// What an endpoint answers when the plugin cannot open its store, and what it
// answers when the store opens and holds nothing.
//
// Issue #31's third condition has two halves. The settings page half landed
// separately; this is the other one, that the endpoints return a status saying
// the plugin is unavailable rather than one saying the data is empty. The two
// halves are worth keeping apart in a reader's head: the page is for whoever
// can fix the file, and this is for whoever asked a question and would
// otherwise be told, in a well formed answer, that they watched nothing.

using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The year endpoint, driven with a read that cannot open the store.
/// </summary>
public class EndpointsWhenTheStoreCannotBeOpenedTests
{
    private const int AFinishedYear = 2025;

    /// <summary>
    /// The path a caller sends for their own year.
    /// </summary>
    /// <param name="caller">Who is asking.</param>
    /// <returns>The path.</returns>
    private static string TheirOwnYear(Caller caller)
        => "/Stats/Users/" + caller.UserId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)
            + "/Years/" + AFinishedYear.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A store that will not open is answered as the plugin being unavailable,
    /// and the answer carries no year at all.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnsweredAsUnavailable()
    {
        using var endpoints = new InProcessEndpoints(
            (_, _, _, _) => throw new StoreCouldNotBeOpenedException(new InvalidOperationException("the file")));

        var answer = await endpoints.Get(TheirOwnYear(Caller.Someone), Caller.Someone);

        Assert.Equal(503, answer.Status);

        // Not an empty year in a 503's clothing either. A body carrying the
        // shape of an answer is a body something will draw.
        Assert.DoesNotContain("\"plays\"", answer.Body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A store that opens and holds nothing is an answer, and it is a different
    /// answer from the one above.
    /// </summary>
    /// <remarks>
    /// This is the half of the condition that the case above cannot carry on
    /// its own. An endpoint that answered 503 whenever it had no rows would
    /// pass that case and be wrong about every quiet year, and the two cases
    /// together are what say the plugin tells those apart.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AYearWithNoPlaysIsAnAnswerRatherThanAnOutage()
    {
        using var endpoints = new InProcessEndpoints(
            (userId, year, zone, topCount) => YearInReview.Over([], userId, year, zone, topCount, null));

        var answer = await endpoints.Get(TheirOwnYear(Caller.Someone), Caller.Someone);

        Assert.Equal(200, answer.Status);
    }

    /// <summary>
    /// Anything else a read throws is left alone, so a defect in the plugin is
    /// not reported to a caller as a store that is briefly away.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AFailureThatIsNotTheStoreIsNotDressedUpAsOne()
    {
        using var endpoints = new InProcessEndpoints(
            (_, _, _, _) => throw new NotSupportedException("a defect"));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => endpoints.Get(TheirOwnYear(Caller.Someone), Caller.Someone));
    }

    /// <summary>
    /// A store that will not open does not widen who may ask. A caller who
    /// would have been refused is still refused, and the refusal is the answer
    /// rather than the outage.
    /// </summary>
    /// <remarks>
    /// The order this proves is the one worth having a case for: the 503 is
    /// reached only after the caller has been let through, so an outage never
    /// stands in for an authorization check that did not run. It also keeps the
    /// outage from being a way to ask whether an account exists.
    /// </remarks>
    /// <param name="callerName">Who is asking.</param>
    /// <param name="expected">What they get.</param>
    /// <returns>The running case.</returns>
    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("a different ordinary user", 403)]
    public async Task WhoMayAskIsSettledBeforeTheStoreIsReached(string callerName, int expected)
    {
        using var endpoints = new InProcessEndpoints(
            (_, _, _, _) => throw new StoreCouldNotBeOpenedException(new InvalidOperationException("the file")));

        var caller = System.Linq.Enumerable.Single(
            Caller.All,
            shape => string.Equals(shape.Name, callerName, StringComparison.Ordinal));

        var answer = await endpoints.Get(TheirOwnYear(Caller.Someone), caller);

        Assert.Equal(expected, answer.Status);
    }

    /// <summary>
    /// A year the endpoint does not answer for is still refused as such, so the
    /// bound in front of the fold is not softened by a store that is away.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AYearOutsideTheBoundIsStillNotFound()
    {
        using var endpoints = new InProcessEndpoints(
            (_, _, _, _) => throw new StoreCouldNotBeOpenedException(new InvalidOperationException("the file")));

        var answer = await endpoints.Get(
            "/Stats/Users/" + Caller.Someone.UserId.ToString("D", System.Globalization.CultureInfo.InvariantCulture)
                + "/Years/1969",
            Caller.Someone);

        Assert.Equal(404, answer.Status);
    }
}
