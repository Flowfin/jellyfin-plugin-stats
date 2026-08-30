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
    /// Every value any action in this plugin takes off a request is one this
    /// file has admitted by name.
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
    /// <para>
    /// The set below is closed and every entry has to earn its place, because a
    /// list that grows whenever a run goes red is a list that records what
    /// happened rather than what is allowed. Each entry says what it is.
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
            "to",

            // What a person is saying about themselves, on the consent
            // endpoint. It is neither an identity nor a window and it is not
            // what this condition is against either: a filter or a sort decides
            // which rows the server reaches for, and this decides nothing about
            // rows at all. It is admitted by name so the set stays closed, and
            // that is the whole of what the set is worth. Issue #42.
            "answer",

            // The filter and the sort on the aggregate top list, and the first
            // two entries here that ARE what issue #55's second condition is
            // about. They are admitted because each maps through a
            // ClosedSet<T> and a value in neither set is refused before the
            // store is opened, which is what that condition asks for, and
            // because neither reaches the store as anything but a member of an
            // enumeration this build declares. The set they map through is
            // driven at ClosedSetTests and the refusal is driven at
            // TheAggregateTopListTests.
            "grouping",
            "order",

            // What a breakdown groups by, on the aggregate route. It is the
            // same case as the two above and is admitted for the same reason:
            // it maps through a ClosedSet<T> whose members come from an
            // enumeration the account is absent from, so a request cannot ask
            // to be shown people whatever it writes here.
            "dimension",

            // Which stretch of time a person's own figures are read over. It is
            // the same case again and admitted for the same reason: three names
            // mapped through a ClosedSet<T>, refused before the store is opened,
            // and never reaching the store as anything but a member of an
            // enumeration this build declares. It is NOT a range, which is the
            // shape that would let a caller decide how much of the store one
            // request reads. Issue #274.
            "window"
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
            "These action parameters are not in the set this file admits, and issue #55 asks that a "
            + "filter or a sort map through a closed set rather than arriving as a value nobody declared: "
            + string.Join(", ", unexpected));
    }


    /// <summary>
    /// No action takes an enumeration off a request, so a closed choice cannot
    /// be bound straight out of the query.
    /// </summary>
    /// <remarks>
    /// THIS IS THE ONE-WORD MISTAKE ISSUE #55'S SECOND CONDITION IS LOST TO,
    /// and it is available on every filter and every sort somebody will ever
    /// add here. An action declaring <c>[FromQuery] TopListOrder order</c> reads
    /// as closed: the type has two members, the framework refuses a member name
    /// it does not know, and every reader of the diff nods. What the framework
    /// does with a NUMBER is the part nobody pictures, and it was measured here
    /// rather than assumed. A number outside the members is refused; a member's
    /// OWN number is not. <c>?order=0</c> and <c>?order=1</c> arrive as the two
    /// members, and <c>?order=</c> arrives as nothing and takes the default. So
    /// the endpoint answers a vocabulary nobody declared, one that changes
    /// meaning when a member is reordered, and no reading of the source says
    /// so.
    /// <para>
    /// Refused structurally rather than by a case per parameter, because the
    /// case per parameter is exactly what somebody adding the next one forgets.
    /// A choice reaches an action as a string and is mapped by
    /// <see cref="ClosedSet{T}"/>, which knows only the spellings it was given.
    /// </para>
    /// <para>
    /// What this cannot see is a closed choice arriving as a number on purpose,
    /// an <c>int</c> parameter a caller indexes a column list with. That is a
    /// name judgement, which is the same bound the greppable rule's own record
    /// states, and this walk does not claim it.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoActionTakesAnEnumerationOffARequest()
    {
        var bound = typeof(YourYearController).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .SelectMany(m => m.GetParameters().Select(parameter => new
            {
                Where = Name(m) + "." + parameter.Name,
                Type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType
            }))
            .ToList();

        Assert.NotEmpty(bound);

        var enumerations = bound.Where(parameter => parameter.Type.IsEnum).Select(parameter => parameter.Where).ToList();

        Assert.True(
            enumerations.Count == 0,
            "These action parameters bind an enumeration straight off the request, and a number outside the "
            + "declared members binds to it without being refused: "
            + string.Join(", ", enumerations));
    }

    private static string Name(MethodInfo action)
        => action.DeclaringType!.Name + "." + action.Name;

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

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit)
        {
            var going = Math.Min(_left, limit);
            _left -= going;
            return going;
        }

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
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

        public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

        public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

        public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

        public ConsentRecord? ConsentFor(Guid userId) => throw NotPartOfThis();

        public void RecordConsent(ConsentRecord consent) => throw NotPartOfThis();

        public void ForgetConsentFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        // A rollup this store never kept. The same refusal as the reads above
        // and for the same reason: answering with none would let a caller that
        // asked about days pass through a fake that has none.
        public TimeZoneInfo? RollupZone => throw NotPartOfThis();

        public IEnumerable<DailyRollup> AllRollups() => throw NotPartOfThis();


        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithConsent() => throw NotPartOfThis();

        public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => throw NotPartOfThis();

        public void RebuildRollups() => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("This store answers only what a deletion asks.");
    }
}
