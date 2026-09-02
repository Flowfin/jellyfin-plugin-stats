// The endpoint by which one account removes its own plays, driven over the
// in-process route.
//
// The matrix beside this file says who gets which status. This says what the
// endpoint does with the request once it has decided that: which window reaches
// the removal, which requests are refused before anything is opened, and what a
// store that will not open comes back as.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the deletion endpoint answers, and what it refuses.
/// </summary>
public class DeleteYourOwnHistoryTests
{
    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A caller with no window named removes everything they have, and the body
    /// says how much went. A success carrying no number leaves a page saying
    /// "done" over a request that may have matched nothing.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ACallerRemovesTheirWholeHistory()
    {
        var store = new RemovingStore(rowsForTheAccount: 4);

        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => store, OwnHistoryDeletion.DefaultBite));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(4, body.RootElement.GetProperty("removed").GetInt32());
        Assert.Empty(store.Windows);
    }

    /// <summary>
    /// The two instants on the request reach the removal as the moments they
    /// name, in UTC, and not as whatever a machine reading them would have made
    /// of them. The request states its own offset, so the same window sent from
    /// two places is one window.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheWindowOnTheRequestIsTheWindowThatIsDeleted()
    {
        var store = new RemovingStore(rowsForTheAccount: 1);

        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => store, OwnHistoryDeletion.DefaultBite));

        var who = Caller.Someone;

        // Noon in a zone two hours ahead of UTC, so a reader that took the
        // written digits rather than the instant would delete a window two
        // hours out of place.
        var answer = await endpoints.Send(
            "DELETE",
            Path(who.UserId) + "?from=2026-03-14T11:00:00%2B02:00&to=2026-03-15T11:00:00%2B02:00",
            who);

        Assert.Equal(200, answer.Status);

        var window = Assert.Single(store.Windows);

        Assert.Equal(new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc), window.From);
        Assert.Equal(new DateTime(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc), window.To);
    }

    /// <summary>
    /// One end of a window without the other is refused, and nothing is opened.
    /// Guessing which end was left out is a guess about somebody's history, and
    /// the guess that reads best is the one that deletes the most.
    /// </summary>
    /// <param name="query">The half window on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("?from=2026-03-14T00:00:00Z")]
    [InlineData("?to=2026-03-15T00:00:00Z")]
    public async Task HalfAWindowIsRefusedAndNothingIsOpened(string query)
    {
        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => throw new IOException("Nothing should be opened."), 1));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId) + query, who);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A window that ends at or before it begins is refused at the endpoint
    /// rather than answered with nought, because nought is what an empty window
    /// answers and a caller who swapped their bounds would read that as their
    /// history being empty.
    /// </summary>
    /// <param name="to">The end of the window.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData("2026-03-14T00:00:00Z")]
    [InlineData("2026-03-13T00:00:00Z")]
    public async Task AWindowThatEndsBeforeItBeginsIsRefused(string to)
    {
        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(() => throw new IOException("Nothing should be opened."), 1));

        var who = Caller.Someone;

        var answer = await endpoints.Send(
            "DELETE",
            Path(who.UserId) + "?from=2026-03-14T00:00:00Z&to=" + to,
            who);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is a status rather than a success over
    /// nothing. An answer saying no rows went, from a plugin that never reached
    /// the file, is the one wrong answer this endpoint must not give: the
    /// caller reads it as their history already being gone.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatCannotBeOpenedIsNotAnAnswerOfNought()
    {
        using var endpoints = new InProcessEndpoints(
            deletion: new OwnHistoryDeletion(
                () => throw new IOException("The store is not there."),
                OwnHistoryDeletion.DefaultBite));

        var who = Caller.Someone;
        var answer = await endpoints.Send("DELETE", Path(who.UserId), who);

        Assert.Equal(503, answer.Status);
    }

    /// <summary>
    /// The endpoint refuses to be built without the two things it cannot work
    /// without, rather than failing on the first request that reaches it.
    /// </summary>
    [Fact]
    public void TheEndpointRefusesToBeBuiltOnNothing()
    {
        var deletion = new OwnHistoryDeletion(() => new RemovingStore(0), 1);

        Assert.Throws<ArgumentNullException>(() => new Jellyfin.Plugin.Stats.Api.YourHistoryController(null!, null!));
        Assert.Throws<ArgumentNullException>(
            () => new Jellyfin.Plugin.Stats.Api.YourHistoryController(deletion, null!));
    }

    private static string Path(Guid userId)
        => "/Stats/Users/" + userId.ToString("D", CultureInfo.InvariantCulture) + "/Plays";

    /// <summary>
    /// A store that removes what it is asked to and records the windows it was
    /// asked about.
    /// </summary>
    private sealed class RemovingStore : IPlayStore
    {
        private readonly List<(DateTime From, DateTime To)> _windows = new();

        private int _rowsLeft;

        public RemovingStore(int rowsForTheAccount)
        {
            _rowsLeft = rowsForTheAccount;
        }

        /// <summary>
        /// Gets the windows this store was asked to delete inside, in order.
        /// </summary>
        public IReadOnlyList<(DateTime From, DateTime To)> Windows => _windows;

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit) => Bite(limit);

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
        {
            if (_rowsLeft > 0)
            {
                _windows.Add((fromUtc, toUtc));
            }

            return Bite(limit);
        }

        public void ReclaimFreedSpace()
        {
        }

        public void Dispose()
        {
        }

        public void Add(PlayRecord play) => throw NotPartOfThis();

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

        public long CountRollupsBefore(DateOnly day) => throw NotPartOfThis();

        public int DeleteRollupsBefore(DateOnly day, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => throw NotPartOfThis();

        public void RebuildRollups() => throw NotPartOfThis();

        public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

        public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

        public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

        public ConsentRecord? ConsentFor(Guid userId) => throw NotPartOfThis();

        public void RecordConsent(ConsentRecord consent) => throw NotPartOfThis();

        public void ForgetConsentFor(Guid userId) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("This store answers only what a deletion of one account's own history asks.");

        private int Bite(int limit)
        {
            var taken = Math.Min(_rowsLeft, limit);
            _rowsLeft -= taken;

            return taken;
        }
    }
}
