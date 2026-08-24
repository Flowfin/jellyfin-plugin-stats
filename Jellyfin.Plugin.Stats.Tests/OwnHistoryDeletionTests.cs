// The removal one account asks for over its own history, driven over a
// temporary directory. Nothing here needs a server: the store takes the folder
// it writes into as an argument and the deletion takes the function that opens
// one.
//
// Every case that asserts a deletion reads the rows back through a store opened
// afresh over the same file, and through AllPlays, which answers to nobody's
// filter. That is the third condition of issue #46: it tells a deletion from a
// row a filtered read happened to skip, which a reader that took the account as
// an argument could not.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class OwnHistoryDeletionTests : IDisposable
{
    /// <summary>
    /// The account asking in every case here.
    /// </summary>
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    /// <summary>
    /// The account that is not asking, and whose rows every case checks are
    /// still there. A deletion that emptied the table would pass every
    /// assertion about Alice.
    /// </summary>
    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public OwnHistoryDeletionTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// Every row the account has goes when it names no window, and nobody
    /// else's does.
    /// </summary>
    [Fact]
    public void EveryPlayOfTheAccountGoesWhenNoWindowIsNamed()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice, March));
            store.Add(APlayBy(Bob, March));
            store.Add(APlayBy(Alice, March.AddDays(200)));
        }

        Assert.Equal(2, ADeletion().Delete(Alice, null, null));

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.PlaysFor(Alice));
        Assert.Equal(new[] { Bob }, after.AllPlays().Select(play => play.UserId));
    }

    /// <summary>
    /// A window takes the rows that started inside it and leaves the rest,
    /// including the rows of the same account either side of it.
    /// </summary>
    [Fact]
    public void OnlyThePlaysInsideTheWindowGo()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice, March.AddDays(-1)));
            store.Add(APlayBy(Alice, March));
            store.Add(APlayBy(Alice, March.AddDays(1)));
            store.Add(APlayBy(Bob, March));
        }

        Assert.Equal(1, ADeletion().Delete(Alice, March, March.AddHours(1)));

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(
            new[] { March.AddDays(-1), March.AddDays(1) },
            after.PlaysFor(Alice).Select(play => play.StartedUtc).OrderBy(started => started));

        Assert.Single(after.PlaysFor(Bob));
    }

    /// <summary>
    /// The window is half open, so a play starting on its last instant stays
    /// and two windows laid end to end delete each row once. A closed window
    /// would take the second row here twice over the two calls, which on a
    /// deletion is a row somebody expected to keep.
    /// </summary>
    [Fact]
    public void APlayStartingAtTheEndOfTheWindowStays()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice, March));
            store.Add(APlayBy(Alice, March.AddHours(1)));
        }

        Assert.Equal(1, ADeletion().Delete(Alice, March, March.AddHours(1)));

        using var after = new SqlitePlayStore(_root);

        Assert.Equal(new[] { March.AddHours(1) }, after.PlaysFor(Alice).Select(play => play.StartedUtc));
    }

    /// <summary>
    /// A set larger than one bite goes entirely, on both routes. The statements
    /// the store runs are bounded, so a deletion that ran one once would leave
    /// the remainder behind and report a number that made it look like it had
    /// worked.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MoreRowsThanOneBiteAllGo(bool inAWindow)
    {
        using (var store = new SqlitePlayStore(_root))
        {
            for (var i = 0; i < 5; i++)
            {
                store.Add(APlayBy(Alice, March.AddMinutes(i)));
            }

            store.Add(APlayBy(Bob, March));
        }

        var deletion = ADeletion(bite: 2);

        var removed = inAWindow
            ? deletion.Delete(Alice, March, March.AddHours(1))
            : deletion.Delete(Alice, null, null);

        Assert.Equal(5, removed);

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.PlaysFor(Alice));
        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// The first condition of issue #46. Their own detail view is empty
    /// afterwards and the figures over what is left hold none of their plays,
    /// and both are read off a fold rather than off the rows, because a fold is
    /// what a reader is shown and the rows are what the case just changed.
    /// </summary>
    [Fact]
    public void TheirOwnYearIsEmptyAfterwardsAndTheFiguresHoldNoneOfIt()
    {
        var march2025 = new DateTime(2025, 3, 14, 9, 0, 0, DateTimeKind.Utc);

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice, march2025));
            store.Add(APlayBy(Alice, march2025.AddDays(1)));
            store.Add(APlayBy(Bob, march2025));
        }

        using (var before = new SqlitePlayStore(_root))
        {
            Assert.True(AYearOf(before, Alice, 2025).AnythingRecorded);
            Assert.Equal(3, DeliveryMethodShares.Over(before.AllPlays()).Plays);
        }

        ADeletion().Delete(Alice, null, null);

        using var after = new SqlitePlayStore(_root);

        var year = AYearOf(after, Alice, 2025);

        Assert.False(year.AnythingRecorded);
        Assert.Null(year.Plays);
        Assert.Equal(1, DeliveryMethodShares.Over(after.AllPlays()).Plays);
    }

    /// <summary>
    /// An account with nothing in what it named is answered with nought rather
    /// than refused, and the file is not rewritten for it. A rewrite reclaiming
    /// no pages is the whole cost of a reclaim for no reason, and this is the
    /// ordinary case for somebody asking twice.
    /// </summary>
    [Fact]
    public void NothingToDeleteIsNoughtAndNoReclaim()
    {
        var store = new CountingPlayStore(rowsForTheAccount: 0);

        Assert.Equal(0, new OwnHistoryDeletion(() => store, OwnHistoryDeletion.DefaultBite).Delete(Alice, null, null));

        Assert.Equal(0, store.Reclaims);
        Assert.True(store.Disposed);
    }

    /// <summary>
    /// The space goes back to the file system once, at the end, rather than
    /// after every bite. A delete leaves the row's bytes in a page the file has
    /// stopped pointing at, and a page nothing points at is still in the file
    /// for anybody reading it, which is what somebody asking for their history
    /// to be gone is asking about.
    /// </summary>
    [Fact]
    public void TheSpaceIsGivenBackOnceWhenSomethingWent()
    {
        var store = new CountingPlayStore(rowsForTheAccount: 5);

        Assert.Equal(5, new OwnHistoryDeletion(() => store, 2).Delete(Alice, null, null));

        Assert.Equal(1, store.Reclaims);
        Assert.True(store.Disposed);
    }

    /// <summary>
    /// A window is named by both of its ends or by neither. One end alone does
    /// not say which rows were meant, and guessing which end was left out is a
    /// guess about somebody's history.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HalfAWindowIsRefused(bool theStartWasGiven)
    {
        var deletion = new OwnHistoryDeletion(() => throw new IOException("Nothing should be opened."), 1);

        Assert.Throws<ArgumentException>(() => deletion.Delete(
            Alice,
            theStartWasGiven ? March : null,
            theStartWasGiven ? null : March));
    }

    /// <summary>
    /// A window that ends at or before it begins is refused by the store rather
    /// than answered with nought. Nought is what an empty window answers, and a
    /// caller who swapped their two bounds would read that as their history
    /// having nothing in it and stop asking.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AWindowThatEndsBeforeItBeginsIsRefused(int hours)
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysFor(Alice, March, March.AddHours(hours), 10));
    }

    /// <summary>
    /// Neither bound is taken unless it says it is in UTC. A local moment read
    /// as UTC moves the boundary by the machine's offset, which on a deletion
    /// is rows nobody asked to lose at one end and rows somebody asked to lose
    /// and still has at the other.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local, DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Utc, DateTimeKind.Unspecified)]
    public void ABoundThatDoesNotSayItIsUtcIsRefused(DateTimeKind start, DateTimeKind end)
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentException>(() => store.DeletePlaysFor(
            Alice,
            DateTime.SpecifyKind(March, start),
            DateTime.SpecifyKind(March.AddHours(1), end),
            10));
    }

    /// <summary>
    /// The store never deletes more rows than it was asked for, and being asked
    /// for none is a mistake rather than a deletion of nothing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AWindowedDeletionOfNoRowsAtAllIsRefused(int limit)
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysFor(Alice, March, March.AddHours(1), limit));
    }

    /// <summary>
    /// The deletion refuses to be built without something to open, and without
    /// a bite it could take.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TheDeletionRefusesToBeBuiltOnNothing(int bite)
    {
        Assert.Throws<ArgumentNullException>(() => new OwnHistoryDeletion(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OwnHistoryDeletion(() => new CountingPlayStore(0), bite));
    }

    /// <summary>
    /// A store that cannot be opened faults with the type an endpoint answers
    /// with a status for, rather than reporting a clean deletion of nothing.
    /// Nothing and nought are different facts about somebody's history, and the
    /// second is the one that reads as success.
    /// </summary>
    [Fact]
    public void AStoreThatCannotBeOpenedFaultsRatherThanReportingNought()
    {
        var deletion = new OwnHistoryDeletion(
            () => throw new IOException("The store is not there."),
            OwnHistoryDeletion.DefaultBite);

        var refusal = Assert.Throws<StoreCouldNotBeOpenedException>(() => deletion.Delete(Alice, null, null));

        Assert.IsType<IOException>(refusal.InnerException);
    }

    /// <summary>
    /// The first condition of issue #46, in the half about the detail view. A
    /// held year is a detail view answered without reading a row, so a deletion
    /// that took the rows and left the hold would hand the caller their own
    /// year back afterwards, complete and looking correct, with nothing in it
    /// drawn from anything that still exists.
    /// </summary>
    [Fact]
    public void TheFoldedYearsOfThatAccountAreLetGo()
    {
        var folds = new CountingFold();
        var held = new HeldYears(folds.Fold, new FixedClock(new DateTimeOffset(March)));

        // A year the server has finished, because an unfinished one is folded
        // again on every ask and would make every count here a four.
        held.For(Alice, 2024, TimeZoneInfo.Utc, 10);
        held.For(Bob, 2024, TimeZoneInfo.Utc, 10);

        new OwnHistoryDeletion(() => new CountingPlayStore(1), OwnHistoryDeletion.DefaultBite, held)
            .Delete(Alice, null, null);

        held.For(Alice, 2024, TimeZoneInfo.Utc, 10);
        held.For(Bob, 2024, TimeZoneInfo.Utc, 10);

        // Three folds and not four: Alice's year was folded again because the
        // hold went with her rows, and Bob's was answered from the hold it was
        // already in.
        Assert.Equal(3, folds.Calls);
    }

    /// <summary>
    /// A window drops every held year of that account rather than the years it
    /// touched. Dropping more than was deleted is the safe direction, and it is
    /// the answer the account deletion already gives.
    /// </summary>
    [Fact]
    public void AWindowStillLetsGoOfEveryHeldYearOfThatAccount()
    {
        var folds = new CountingFold();
        var held = new HeldYears(folds.Fold, new FixedClock(new DateTimeOffset(March)));

        held.For(Alice, 2024, TimeZoneInfo.Utc, 10);

        new OwnHistoryDeletion(() => new CountingPlayStore(1), OwnHistoryDeletion.DefaultBite, held)
            .Delete(Alice, March, March.AddHours(1));

        held.For(Alice, 2024, TimeZoneInfo.Utc, 10);

        Assert.Equal(2, folds.Calls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static PlayRecord APlayBy(Guid userId, DateTime startedUtc)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
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
                Reasons = Array.Empty<string>()
            }
        };
    }

    private static YearInReview AYearOf(IPlayStore store, Guid userId, int year)
        => YearInReview.Over(
            store.PlaysFor(userId),
            userId,
            year,
            TimeZoneInfo.Utc,
            10,
            store.OldestPlayStartedUtc());

    private OwnHistoryDeletion ADeletion(int bite = OwnHistoryDeletion.DefaultBite)
        => new(() => new SqlitePlayStore(_root), bite);

    /// <summary>
    /// A fold that counts how often it was asked, so a case can tell an answer
    /// that was folded again from one that came out of the hold.
    /// </summary>
    private sealed class CountingFold
    {
        public int Calls { get; private set; }

        public YearInReview Fold(Guid userId, int year, TimeZoneInfo zone, int topCount)
        {
            Calls++;

            return YearInReview.Over([], userId, year, zone, topCount, null);
        }
    }

    /// <summary>
    /// A store that counts what was asked of it. The two cases about the
    /// reclaim are about a call being made or not made, and a real store
    /// answers both of them with a file whose size a test would then have to
    /// read a meaning into.
    /// </summary>
    private sealed class CountingPlayStore : IPlayStore
    {
        private int _rowsLeft;

        public CountingPlayStore(int rowsForTheAccount)
        {
            _rowsLeft = rowsForTheAccount;
        }

        /// <summary>
        /// Gets how many times the space was given back.
        /// </summary>
        public int Reclaims { get; private set; }

        /// <summary>
        /// Gets whether the deletion disposed of this store.
        /// </summary>
        public bool Disposed { get; private set; }

        public int DeletePlaysFor(Guid userId, int limit) => Bite(limit);

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit) => Bite(limit);

        public void ReclaimFreedSpace() => Reclaims++;

        public void Dispose() => Disposed = true;

        public void Add(PlayRecord play) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit) => throw NotPartOfThis();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

        public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

        public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

        public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, int limit) => throw NotPartOfThis();

        public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

        public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

        public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

        public ConsentRecord? ConsentFor(Guid userId) => throw NotPartOfThis();

        public void RecordConsent(ConsentRecord consent) => throw NotPartOfThis();

        public void ForgetConsentFor(Guid userId) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("A deletion of one account's own history reads nothing and writes nothing, so this fake answers nothing else.");

        private int Bite(int limit)
        {
            var taken = Math.Min(_rowsLeft, limit);
            _rowsLeft -= taken;

            return taken;
        }
    }
}
