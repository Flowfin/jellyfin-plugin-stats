// The consumer that removes a deleted account's rows, driven over a temporary
// directory. Nothing here needs a server: the event carries the user, the store
// takes the folder it writes into as an argument, and the consumer takes the
// function that opens one.
//
// Every case that asserts a deletion reads the rows back through a store opened
// afresh over the same file, and through AllPlays, which answers to nobody's
// filter. That is what tells a deletion from a row the read happened to skip,
// and it is the third condition of issue #46 arriving early because the same
// reader proves both.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Events;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class UserDeletedTests : IDisposable
{
    /// <summary>
    /// The account that is deleted in every case here.
    /// </summary>
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    /// <summary>
    /// The account that is not, and whose rows every case checks are still
    /// there. A deletion that took the whole table would pass every assertion
    /// about Alice.
    /// </summary>
    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private readonly string _root;

    public UserDeletedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #44, in the half that has something to
    /// count. The rows are counted for the identifier before and after, through
    /// a store opened again over the same file.
    /// </summary>
    [Fact]
    public async Task EveryRowOfTheDeletedAccountGoesAndNobodyElsesDoes()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice));
            store.Add(APlayBy(Bob));
            store.Add(APlayBy(Alice));
        }

        using (var before = new SqlitePlayStore(_root))
        {
            Assert.Equal(2, before.PlaysFor(Alice).Count());
        }

        await AConsumer().OnEvent(TheDeletionOf(Alice));

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.PlaysFor(Alice));
        Assert.Equal(new[] { Bob }, after.AllPlays().Select(play => play.UserId));
    }

    /// <summary>
    /// A set larger than one bite goes entirely. The statement the store runs
    /// is bounded, so a consumer that ran it once would leave the remainder
    /// behind and report nothing: the rows it did delete would make the
    /// deletion look like it worked.
    /// </summary>
    [Fact]
    public async Task MoreRowsThanOneBiteAllGo()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            for (var i = 0; i < 5; i++)
            {
                store.Add(APlayBy(Alice));
            }

            store.Add(APlayBy(Bob));
        }

        await AConsumer(bite: 2).OnEvent(TheDeletionOf(Alice));

        using var after = new SqlitePlayStore(_root);

        Assert.Empty(after.PlaysFor(Alice));
        Assert.Single(after.AllPlays());
    }

    /// <summary>
    /// The second condition of issue #44. An aggregate computed again after the
    /// deletion is computed over what is left, so the deleted account's plays
    /// are not in it. The figures are read off the fold rather than off the
    /// rows, because a report is what an administrator sees and the rows are
    /// what this test just changed.
    /// </summary>
    [Fact]
    public async Task TheFiguresRecomputedAfterwardsHoldOnlyWhatIsLeft()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Alice, PlayMethod.Transcode));
            store.Add(APlayBy(Alice, PlayMethod.Transcode));
            store.Add(APlayBy(Bob, PlayMethod.DirectPlay));
        }

        using (var before = new SqlitePlayStore(_root))
        {
            var shares = DeliveryMethodShares.Over(before.AllPlays());

            Assert.Equal(3, shares.Plays);
            Assert.Equal(2, shares.Transcode);
        }

        await AConsumer().OnEvent(TheDeletionOf(Alice));

        using var after = new SqlitePlayStore(_root);
        var recomputed = DeliveryMethodShares.Over(after.AllPlays());

        Assert.Equal(1, recomputed.Plays);
        Assert.Equal(0, recomputed.Transcode);
        Assert.Equal(1, recomputed.DirectPlay);
    }

    /// <summary>
    /// The rows are gone from the file rather than from the table alone. A
    /// delete leaves the bytes in a page nothing points at any more, and an
    /// account deletion that left them there would be the soft delete this
    /// issue refuses, one layer down.
    /// </summary>
    [Fact]
    public async Task TheSpaceTheRowsHeldIsGivenBack()
    {
        var store = new CountingPlayStore(rowsPerUser: 1);

        await AConsumerOver(store).OnEvent(TheDeletionOf(Alice));

        Assert.Equal(1, store.Reclaims);
        Assert.True(store.Disposed);
    }

    /// <summary>
    /// An account with no rows costs no rewrite of the file. That is the
    /// ordinary case on a server where somebody was excluded from capture or
    /// was added and removed without watching anything, and a rewrite of the
    /// whole store to reclaim nothing is the whole cost for no reason.
    /// </summary>
    [Fact]
    public async Task AnAccountWithNoRowsLeavesTheFileAlone()
    {
        var store = new CountingPlayStore(rowsPerUser: 0);

        await AConsumerOver(store).OnEvent(TheDeletionOf(Alice));

        Assert.Equal(0, store.Reclaims);
        Assert.Equal(1, store.Deletions);
    }

    /// <summary>
    /// The third condition of issue #44. The server asks its container for
    /// every consumer of the event it is about to publish, so what has to
    /// resolve is the interface closed over that event and not this class by
    /// name. A registration of the concrete type alone resolves in a test and
    /// is never called on a server, and nothing else in the system would say
    /// that this had stopped being called.
    /// </summary>
    [Fact]
    public void TheServerFindsThisConsumerForTheEventItPublishes()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        var consumer = Assert.Single(provider.GetServices<IEventConsumer<UserDeletedEventArgs>>());

        Assert.IsType<UserDeletedConsumer>(consumer);
    }

    /// <summary>
    /// A store that cannot be opened faults rather than reporting a clean
    /// deletion. The server catches what a consumer throws and logs it, so the
    /// throw is the only route by which anything outside this plugin learns
    /// that an account's rows are still in the store.
    /// </summary>
    [Fact]
    public async Task AStoreThatCannotBeOpenedFaultsRatherThanPassingQuietly()
    {
        var consumer = new UserDeletedConsumer(
            () => throw new IOException("The store is not there."),
            UserDeletedConsumer.DefaultBite);

        await Assert.ThrowsAsync<IOException>(() => consumer.OnEvent(TheDeletionOf(Alice)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static UserDeletedEventArgs TheDeletionOf(Guid userId)
        => new(FakeUserManager.NewUser("someone", userId));

    private static UserDeletedConsumer AConsumerOver(IPlayStore store)
        => new(() => store, UserDeletedConsumer.DefaultBite);

    private static PlayRecord APlayBy(Guid userId, PlayMethod method = PlayMethod.DirectPlay)
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
            StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = method,
            PlayMethodChangedUtc = null,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    private UserDeletedConsumer AConsumer(int bite = UserDeletedConsumer.DefaultBite)
        => new(() => new SqlitePlayStore(_root), bite);

    /// <summary>
    /// A store that counts what was asked of it. The two cases about the
    /// reclaim are about a call being made or not made, and a real store
    /// answers both of them with a file whose size a test would then have to
    /// read a meaning into.
    /// </summary>
    private sealed class CountingPlayStore : IPlayStore
    {
        private int _rowsLeft;

        public CountingPlayStore(int rowsPerUser)
        {
            _rowsLeft = rowsPerUser;
        }

        /// <summary>
        /// Gets how many times a deletion was asked for, including the last one
        /// that found nothing left.
        /// </summary>
        public int Deletions { get; private set; }

        /// <summary>
        /// Gets how many times the space was given back.
        /// </summary>
        public int Reclaims { get; private set; }

        /// <summary>
        /// Gets whether the consumer disposed of this store.
        /// </summary>
        public bool Disposed { get; private set; }

        public int DeletePlaysFor(Guid userId, int limit)
        {
            Deletions++;

            var taken = Math.Min(_rowsLeft, limit);
            _rowsLeft -= taken;

            return taken;
        }

        public void ReclaimFreedSpace() => Reclaims++;

        public void Dispose() => Disposed = true;

        public void Add(PlayRecord play) => throw NotPartOfThis();

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

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, int limit) => throw NotPartOfThis();

        private static NotSupportedException NotPartOfThis()
            => new("A deletion by identifier reads nothing and writes nothing, so this fake answers nothing else.");
    }
}
