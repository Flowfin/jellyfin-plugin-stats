// What this plugin's endpoints do with a value they cannot read.
//
// Issue #55 asks that no endpoint take a query from the caller, and its second
// condition asks that an unknown value be refused rather than passed through.
// The file beside this one drives the deletion endpoint's own rules: which
// window reaches the removal, and which requests are refused before anything is
// opened. This drives the request the endpoint was never told about, which is
// the one where passing a value through costs somebody their history.
//
// The endpoint under it is the only one in this plugin that takes anything off
// the query today. The year endpoint takes an account and a year off the route
// and nothing else, and the last case here is the reading that says so rather
// than a second subject.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// A value an endpoint cannot read is refused, and is never taken for the
/// absence of the value.
/// </summary>
public class AValueTheEndpointCannotReadTests
{
    /// <summary>
    /// A window parameter that is named and carries nothing is refused, and
    /// nothing is opened.
    /// </summary>
    /// <remarks>
    /// This is the case that costs. Both ends empty binds to no instant at
    /// either end, which is the spelling of "every play the account has", so
    /// before this was refused a form submitted with two empty date fields
    /// removed the caller's whole history and answered 200 with the count.
    /// <para>
    /// One end empty and the other left out is the same fact arriving through
    /// one parameter, and it is here twice on purpose: the endpoint reads each
    /// end separately, so a case naming only the first end proves nothing about
    /// the second. The two cases carrying an instant at one end are held by the
    /// refusal of a half window that already stood here, and they are listed so
    /// a reader sees which spellings reach which refusal.
    /// </para>
    /// </remarks>
    /// <param name="query">The window on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("?from=&to=")]
    [InlineData("?from=")]
    [InlineData("?to=")]
    [InlineData("?from=&to=2026-03-15T00:00:00Z")]
    [InlineData("?from=2026-03-14T00:00:00Z&to=")]
    [InlineData("?from&to")]
    public async Task AWindowParameterCarryingNothingIsRefusedAndNothingIsOpened(string query)
    {
        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => throw new IOException("Nothing should be opened."), 1));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId) + query, who);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A window parameter carrying something that is not an instant is refused,
    /// and nothing is opened.
    /// </summary>
    /// <remarks>
    /// Binding answers this one and the framework returns the status, so what
    /// this holds is that the plugin has put nothing in front of that which
    /// would swallow it, and that the refusal reaches the caller as a refusal
    /// rather than as a window nobody named.
    /// </remarks>
    /// <param name="query">The window on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("?from=banana&to=banana")]
    [InlineData("?from=2026-03-14T00:00:00Z&to=next%20tuesday")]
    [InlineData("?from=0&to=1")]
    public async Task AWindowParameterCarryingSomethingElseIsRefusedAndNothingIsOpened(string query)
    {
        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => throw new IOException("Nothing should be opened."), 1));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId) + query, who);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// Leaving the window out entirely still means every play the account has.
    /// </summary>
    /// <remarks>
    /// The refusal above is worth nothing if it took the one spelling of
    /// "everything" with it, and a guard that refused every deletion would pass
    /// both cases above. This is the near miss for that.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AQueryWithNoWindowInItStillRemovesEverything()
    {
        var store = new CountingStore(rowsForTheAccount: 3);

        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => store, OwnHistoryDeletion.DefaultBite));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(3, body.RootElement.GetProperty("removed").GetInt32());
        Assert.Empty(store.Windows);
    }

    /// <summary>
    /// A parameter the endpoint declares nothing about is ignored rather than
    /// reaching anything, and the request is answered as though it were not
    /// there.
    /// </summary>
    /// <remarks>
    /// This is the shape issue #55 is named for, arriving as a spare query
    /// parameter rather than as a declared one: a caller adding a sort or a
    /// column list to the query gets no say in what the plugin does. It is held
    /// by the action taking two named values and nothing that reads the query
    /// itself, which is what <c>no-query-from-the-request</c> refuses the other
    /// spelling of.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AParameterTheEndpointNeverDeclaredChangesNothing()
    {
        var store = new CountingStore(rowsForTheAccount: 2);

        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => store, OwnHistoryDeletion.DefaultBite));

        var who = Caller.Someone;

        var answer = await endpoints.Send(
            "DELETE",
            Path(who.UserId) + "?orderBy=Removed%20DESC&columns=UserId&limit=1",
            who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(2, body.RootElement.GetProperty("removed").GetInt32());
        Assert.Empty(store.Windows);
    }

    /// <summary>
    /// Every value any action in this plugin takes off a request is either an
    /// identity the route carries or one of the two window instants.
    /// </summary>
    /// <remarks>
    /// The condition this file serves is about every filter and sort parameter,
    /// so it is worth reading the set rather than the two endpoints somebody
    /// remembered. The walk is by reflection over the actions, so an endpoint
    /// added tomorrow with a parameter named for a column reddens this without
    /// anybody adding a case to it.
    /// <para>
    /// What it cannot do is judge a name. A parameter called <c>scope</c>
    /// carrying a column list would pass here and pass the greppable rule as
    /// well, which is why that rule's own record says the shape is held and the
    /// meaning is not.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoActionTakesAnythingButAnIdentityOrAWindow()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "userId",
            "year",
            "from",
            "to"
        };

        var taken = typeof(YourYearController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .SelectMany(m => m.GetParameters())
            .Select(p => p.Name!)
            .ToList();

        Assert.NotEmpty(taken);

        var unexpected = taken.Where(n => !allowed.Contains(n)).Distinct().ToList();

        Assert.True(
            unexpected.Count == 0,
            "These action parameters are neither an identity nor a window, and issue #55 asks that a "
            + "filter or a sort map through a closed set before one of either exists: "
            + string.Join(", ", unexpected));
    }

    private static string Path(Guid userId)
        => "/Stats/Users/" + userId.ToString("D", CultureInfo.InvariantCulture) + "/Plays";

    /// <summary>
    /// A store that counts what it was asked to remove and records the windows
    /// it was given.
    /// </summary>
    private sealed class CountingStore : IPlayStore
    {
        private int _left;

        public CountingStore(int rowsForTheAccount) => _left = rowsForTheAccount;

        public List<(DateTime From, DateTime To)> Windows { get; } = [];

        public int DeletePlaysFor(Guid userId, int limit)
        {
            var going = Math.Min(_left, limit);
            _left -= going;
            return going;
        }

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit)
        {
            Windows.Add((fromUtc, toUtc));
            var going = Math.Min(_left, limit);
            _left -= going;
            return going;
        }

        public void ReclaimFreedSpace()
        {
        }

        public void Dispose()
        {
        }

        public void Add(PlayRecord play) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("This store answers only what a deletion asks.");
    }
}
