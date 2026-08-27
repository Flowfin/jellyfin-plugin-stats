// What each of this plugin's five deletions says about the rows it takes, read
// back off the store rather than off the method that was called. Issue #251.
//
// Every case here drives a real store over a temporary directory, because the
// thing being proved is that the class survives the deletion: a fake that
// recorded the argument would prove the argument was passed and say nothing
// about what a reader arriving after the rows have gone can still see.
//
// The five call sites are driven through the classes that hold them rather than
// by calling the store directly. That is what makes those cases bite: changing
// the class named at a call site is a change to one of those files, and a case
// that called the store itself would go on passing.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Events;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class DeletionClassTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>
    /// The moment every case here is written around, fixed rather than read off
    /// a clock, so the cutoffs below are values a case chose.
    /// </summary>
    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// How many entries a case reads back. Larger than anything a case here
    /// writes, so a case that recorded more than it meant to fails on the
    /// comparison rather than on the bound.
    /// </summary>
    private const int Plenty = 100;

    private readonly string _root;

    public DeletionClassTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The third condition of issue #251. A retention sweep and a corrective
    /// deletion run over one store, and what separates them afterwards is what
    /// the store wrote down rather than which method each one called.
    /// </summary>
    /// <remarks>
    /// The two are made to remove the same number of rows on purpose, so the
    /// only thing telling them apart in the answer is the class. A case whose
    /// two deletions were different sizes would pass over a store that recorded
    /// the size and nothing else.
    /// </remarks>
    [Fact]
    public void ARetentionSweepAndACorrectiveDeletionAreTwoStatements()
    {
        Seed(Alice, March.AddYears(-1));
        Seed(Bob, March);

        new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite)
            .Run(March.AddMonths(-6), new Progress<double>(), CancellationToken.None);

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite).Delete(Bob, null, null);

        using var store = OpenTheStore();

        Assert.Equal(
            new[] { DeletionClass.Corrective, DeletionClass.Retention },
            store.DeletionsRecorded(Plenty).Select(deletion => deletion.Class));

        Assert.All(store.DeletionsRecorded(Plenty), deletion => Assert.Equal(1, deletion.Rows));
    }

    /// <summary>
    /// The sweep that ages rows out names retention. Changing that call site to
    /// the other class fails here.
    /// </summary>
    [Fact]
    public void TheRetentionSweepSaysTheRowsAgedOut()
    {
        Seed(Alice, March.AddYears(-1));

        new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite)
            .Run(March.AddMonths(-6), new Progress<double>(), CancellationToken.None);

        Assert.Equal(DeletionClass.Retention, TheOnlyDeletion().Class);
    }

    /// <summary>
    /// An account the server deleted names the corrective class. Changing that
    /// call site to retention fails here.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AnAccountTheServerDeletedStopsBeingCounted()
    {
        Seed(Alice, March);

        await new UserDeletedConsumer(OpenTheStore, UserDeletedConsumer.DefaultBite)
            .OnEvent(new UserDeletedEventArgs(FakeUserManager.NewUser("someone", Alice)));

        Assert.Equal(DeletionClass.Corrective, TheOnlyDeletion().Class);
    }

    /// <summary>
    /// The sweep over accounts the server no longer has names the corrective
    /// class as well, and it is the one worth a case of its own: it is a
    /// scheduled task on a timer like the retention sweep, so the class it
    /// names is the only thing telling the two apart afterwards.
    /// </summary>
    [Fact]
    public void TheSweepForAccountsTheServerLostStopsCountingThem()
    {
        Seed(Alice, March);

        new UnknownUserSweep(OpenTheStore, new FakeUserManager(), UnknownUserSweep.DefaultBite)
            .Run(new Progress<double>(), CancellationToken.None);

        Assert.Equal(DeletionClass.Corrective, TheOnlyDeletion().Class);
    }

    /// <summary>
    /// A person removing the whole of their own history names the corrective
    /// class.
    /// </summary>
    [Fact]
    public void RemovingYourWholeHistoryStopsItBeingCounted()
    {
        Seed(Alice, March);

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite).Delete(Alice, null, null);

        Assert.Equal(DeletionClass.Corrective, TheOnlyDeletion().Class);
    }

    /// <summary>
    /// So does the same person removing a window of it, which is that class's
    /// second overload. A window inside the retention period is where the
    /// shortcut arrives: those rows were going to age out anyway, and answering
    /// the call as retention would leave every figure that counted them
    /// standing.
    /// </summary>
    [Fact]
    public void RemovingAWindowOfYourHistoryStopsItBeingCounted()
    {
        Seed(Alice, March);

        new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite)
            .Delete(Alice, March.AddHours(-1), March.AddHours(1));

        Assert.Equal(DeletionClass.Corrective, TheOnlyDeletion().Class);
    }

    /// <summary>
    /// A call that took no rows writes nothing down. Every deletion here bites
    /// until one comes back empty, so an entry per call would end each of them
    /// with a row saying that nothing happened.
    /// </summary>
    [Fact]
    public void ADeletionThatTookNoRowsIsNotRecorded()
    {
        Seed(Alice, March);

        using var store = OpenTheStore();

        Assert.Equal(0, store.DeletePlaysFor(Bob, DeletionClass.Corrective, 10));
        Assert.Equal(0, store.DeletePlaysStartedBefore(March.AddYears(-1), DeletionClass.Retention, 10));

        Assert.Empty(store.DeletionsRecorded(Plenty));
    }

    /// <summary>
    /// A number standing where a choice was meant is refused rather than
    /// answered. The argument is required, so the compiler catches a call that
    /// omitted it; this is the other way of failing to choose, and the two
    /// classes say opposite things about the figures over the rows, so there is
    /// no safe one to assume.
    /// </summary>
    /// <param name="undeclared">A value this build has no name for.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void AClassThisBuildHasNoNameForIsRefused(int undeclared)
    {
        Seed(Alice, March);

        using var store = OpenTheStore();
        var deletionClass = (DeletionClass)undeclared;

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysStartedBefore(March.AddYears(1), deletionClass, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysFor(Alice, deletionClass, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysFor(Alice, March.AddHours(-1), March.AddHours(1), deletionClass, 10));

        Assert.Single(store.PlaysFor(Alice));
    }

    /// <summary>
    /// The refusal happens before anything is removed, which is what makes it
    /// worth having: a store that deleted the rows and then refused to record
    /// what the deletion meant would leave exactly the gap this table exists to
    /// explain.
    /// </summary>
    [Fact]
    public void AClassThisBuildHasNoNameForRemovesNothing()
    {
        Seed(Alice, March);

        using var store = OpenTheStore();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => store.DeletePlaysFor(Alice, default, 10));

        Assert.Single(store.PlaysFor(Alice));
        Assert.Empty(store.DeletionsRecorded(Plenty));
    }

    /// <summary>
    /// The read is bounded, and says so by refusing a bound that is not one.
    /// </summary>
    /// <param name="limit">What a caller asked for.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TheReadRefusesABoundThatIsNotOne(int limit)
    {
        using var store = OpenTheStore();

        Assert.Throws<ArgumentOutOfRangeException>(() => store.DeletionsRecorded(limit));
    }

    /// <summary>
    /// Newest first, so a reader asking what has happened since they last
    /// looked reads the near end of the table rather than the far one.
    /// </summary>
    [Fact]
    public void TheDeletionsComeBackNewestFirst()
    {
        Seed(Alice, March);
        Seed(Bob, March.AddYears(-1));

        using var store = OpenTheStore();

        store.DeletePlaysFor(Alice, DeletionClass.Corrective, 10);
        store.DeletePlaysStartedBefore(March.AddMonths(-6), DeletionClass.Retention, 10);

        Assert.Equal(
            new[] { DeletionClass.Retention, DeletionClass.Corrective },
            store.DeletionsRecorded(Plenty).Select(deletion => deletion.Class));

        Assert.Equal(DeletionClass.Retention, Assert.Single(store.DeletionsRecorded(1)).Class);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private DeletionRecorded TheOnlyDeletion()
    {
        using var store = OpenTheStore();

        return Assert.Single(store.DeletionsRecorded(Plenty));
    }

    private SqlitePlayStore OpenTheStore() => new(_root);

    private void Seed(Guid userId, DateTime startedUtc)
    {
        using var store = OpenTheStore();

        store.Add(new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
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
                Reasons = []
            }
        });
    }
}
