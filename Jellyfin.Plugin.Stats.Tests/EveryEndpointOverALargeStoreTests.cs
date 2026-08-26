// What every route and every shape costs over a store nobody would call small.
// Issue #56, third condition.
//
// WHAT A BOUND LIKE THIS IS WORTH, SAID BEFORE IT IS ASSERTED. It is a ceiling
// two orders of magnitude above what the answers actually take here, so it is
// not a performance figure and nothing should be read off it as one. What it
// catches is the class of change that turns an answer into a walk of the table
// per row: a read that lost its index, a fold that opens the store once per
// item, a shape that reads every play to answer about one. Those do not miss a
// generous bound by a little. What it cannot catch is an answer that got twice
// as slow, and a bound tight enough to catch that would be a case that reddens
// on a busy runner for reasons nobody changed.
//
// The store is seeded through the store's own bulk write, which is what makes
// this case possible at all. The two writes were timed against each other over
// a thousand rows on the machine this was written on, and the figures are in
// the body of the pull request rather than here, because a number in a comment
// is a number nothing re-runs. At that rate this seed of ten thousand rows is
// about two minutes one at a time and a fraction of a second in one piece, and
// the case that used to be written here was taken out again for exactly that,
// with what it cost recorded on the issue.
//
// The order the routes are asked in is not arbitrary. The deletion is last,
// because it removes the rows every measurement before it is about.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class EveryEndpointOverALargeStoreTests : IDisposable
{
    /// <summary>
    /// How many plays the store holds while every answer below is measured.
    /// </summary>
    /// <remarks>
    /// Large enough that a shape reading the whole table per row would take
    /// minutes rather than milliseconds, and small enough that the seed itself
    /// is a fraction of a second through the bulk write. This layer's own
    /// ceiling is a good deal higher, and a case seeded to it would be about
    /// the runner's disk rather than about the answers.
    /// </remarks>
    private const int PlaysInTheStore = 10_000;

    /// <summary>
    /// The stated bound every answer below is held to.
    /// </summary>
    /// <remarks>
    /// A ceiling and not a measurement. See the note at the top of this file
    /// for what a number this loose does and does not catch.
    /// </remarks>
    private static readonly TimeSpan LongestAnAnswerMayTake = TimeSpan.FromSeconds(5);

    private static readonly DateTime NewYear = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan BetweenPlays = TimeSpan.FromMinutes(30);

    private readonly string _root;
    private readonly ITestOutputHelper _said;

    /// <summary>
    /// Initializes a new instance of the <see cref="EveryEndpointOverALargeStoreTests"/> class.
    /// </summary>
    /// <param name="said">
    /// Where each measurement is written. A case that only asserts a ceiling
    /// hands a reader nothing to compare against the next run, and the number
    /// this condition asks to be put in a pull request body has to come from
    /// somewhere.
    /// </param>
    public EveryEndpointOverALargeStoreTests(ITestOutputHelper said)
    {
        ArgumentNullException.ThrowIfNull(said);

        _said = said;
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Every route a caller can reach, answered over a large store inside the
    /// stated bound.
    /// </summary>
    /// <returns>The running test.</returns>
    /// <remarks>
    /// Driven as requests rather than as calls, because what this condition is
    /// about is what a caller waits for. The status is asserted beside the
    /// duration: an endpoint that refused instantly would be fast and would be
    /// measuring nothing.
    /// </remarks>
    [Fact]
    public async Task EveryRouteAnswersOverALargeStoreInsideTheBound()
    {
        var who = Caller.Someone;

        Seed(PlaysInTheStore, who.UserId);

        var held = new HeldYears(
            (userId, year, zone, topCount) => ReadFromTheStore.Answering(
                OpenTheStore,
                store => YearInReview.Over(
                    store.PlaysFor(userId),
                    userId,
                    year,
                    zone,
                    topCount,
                    store.OldestPlayStartedUtc())),
            new FixedClock(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) => held.For(userId, year, zone, topCount),
            clock: new FixedClock(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)),
            deletion: new OwnHistoryDeletion(OpenTheStore, OwnHistoryDeletion.DefaultBite, held),
            consent: new ConsentRegister(
                OpenTheStore,
                new FixedClock(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero))));

        var consentPath = Route("Consent", who.UserId);
        var agreeing = string.Format(
            CultureInfo.InvariantCulture,
            "{{\"Agreed\":true,\"WordingVersion\":{0}}}",
            ConsentWording.Version);

        await Within("GET Consent", () => endpoints.Send("GET", consentPath, who)).ConfigureAwait(true);
        await Within("PUT Consent", () => endpoints.Send("PUT", consentPath, who, agreeing)).ConfigureAwait(true);

        var yearPath = Route("Years/2025", who.UserId);

        // Twice, because the first reading folds the year and the second is
        // answered from what was kept. A held answer that had to fold again
        // would be inside the bound here and outside what the hold is for, so
        // both are measured and the second is the one a second caller waits
        // for.
        await Within("GET Years (folded)", () => endpoints.Send("GET", yearPath, who)).ConfigureAwait(true);
        await Within("GET Years (held)", () => endpoints.Send("GET", yearPath, who)).ConfigureAwait(true);

        // Last, because it takes the rows the readings above were over.
        await Within("DELETE Plays", () => endpoints.Send("DELETE", Route("Plays", who.UserId), who)).ConfigureAwait(true);
    }

    /// <summary>
    /// Every shape the query layer answers, over the same store and the same
    /// bound.
    /// </summary>
    /// <remarks>
    /// No route reaches these yet, and the condition says every endpoint. They
    /// are measured anyway rather than left for the day one does: a shape whose
    /// cost nobody has read is a shape that becomes a route and is measured by
    /// whoever it is slow for.
    /// </remarks>
    /// <param name="shape">Which shape to ask.</param>
    [Theory]
    [InlineData("total")]
    [InlineData("series")]
    [InlineData("distribution")]
    [InlineData("breakdown")]
    [InlineData("reasons")]
    [InlineData("top")]
    [InlineData("top by series")]
    public void EveryShapeAnswersOverALargeStoreInsideTheBound(string shape)
    {
        Seed(PlaysInTheStore, Caller.Someone.UserId);

        var queries = new AggregateQueries(OpenTheStore);
        var window = QueryWindow.Of(NewYear, NewYear.AddDays(366));

        object? answer = null;
        var taken = TimeOf(() => answer = Ask(queries, shape, window));

        _said.WriteLine(Said(shape, taken));

        // Beside the duration, because a shape that refused before it folded
        // anything would be fast and would be measuring nothing. Two of these
        // withhold an answer where too few accounts stand behind a row, and
        // that is the shape this assertion catches being timed.
        Assert.NotNull(answer);

        Assert.True(
            taken <= LongestAnAnswerMayTake,
            Said(shape, taken));
    }

    /// <summary>
    /// The bulk write is what makes the cases above possible, and it is one
    /// piece of work rather than one each: a row written on its own is its own
    /// transaction and its own flush.
    /// </summary>
    /// <remarks>
    /// The rows are read back through the walk that answers to nobody's filter,
    /// so what is asserted is that they reached the file and not that a method
    /// returned. The duration is not asserted here; what the difference between
    /// the two writes costs is measured and written into the pull request rather
    /// than turned into a case that reddens on a busy disk.
    /// </remarks>
    [Fact]
    public void ABulkWriteKeepsEveryRowItWasGiven()
    {
        Seed(PlaysInTheStore, Caller.Someone.UserId);

        using var store = OpenTheStore();

        var read = 0;
        foreach (var play in store.AllPlays())
        {
            Assert.Equal(SqlitePlayStore.SchemaVersion, play.SchemaVersion);
            read++;
        }

        Assert.Equal(PlaysInTheStore, read);
    }

    /// <summary>
    /// A bulk write is all or nothing. A sequence with a bad row in the middle
    /// leaves the store as it found it, rather than leaving the rows before it,
    /// which is the difference between this and the loop a caller would write.
    /// </summary>
    [Fact]
    public void ABulkWriteThatCannotFinishKeepsNoneOfIt()
    {
        using (var store = OpenTheStore())
        {
            Assert.ThrowsAny<Exception>(() => store.AddMany(TwoGoodRowsAndABadOne(Caller.Someone.UserId)));
        }

        using var reading = OpenTheStore();

        Assert.Empty(reading.AllPlays());
    }

    /// <summary>
    /// A store that has nothing to gain from the difference owes nothing, and
    /// what it gets is the loop a caller would otherwise have written. Every row
    /// reaches the single write, in the order it was handed over.
    /// </summary>
    /// <remarks>
    /// Driven through the interface rather than through the store on a file,
    /// because that is where the default lives and the store on a file replaces
    /// it. A case that only drove the file would leave the default read by
    /// nobody, which is how an implementation nothing overrides becomes wrong
    /// without anything saying so.
    /// </remarks>
    [Fact]
    public void AStoreThatDoesNotTakeASequenceOfItsOwnGetsTheLoop()
    {
        using var holding = new HoldablePlayStore();
        IPlayStore store = holding;

        var item = new Guid(1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
        var first = APlay(Caller.Someone.UserId, item, NewYear);
        var second = APlay(Caller.Someone.UserId, item, NewYear.AddHours(1));

        store.AddMany([first, second]);

        Assert.Equal([first, second], holding.Rows);
    }

    /// <summary>
    /// A sequence nobody handed over is refused rather than read as none.
    /// </summary>
    [Fact]
    public void ASequenceThatIsNotThereIsRefused()
    {
        using var holding = new HoldablePlayStore();
        IPlayStore store = holding;

        Assert.Throws<ArgumentNullException>(() => store.AddMany(null!));

        using var onAFile = OpenTheStore();

        Assert.Throws<ArgumentNullException>(() => onAFile.AddMany(null!));
    }

    private static object? Ask(AggregateQueries queries, string shape, QueryWindow window)
    {
        var zone = TimeZoneInfo.Utc;

        return shape switch
        {
            "total" => queries.Total(window),
            "series" => queries.Series(window, zone),
            "distribution" => queries.Distribution(window, zone),
            "breakdown" => queries.Breakdown(window, PlayDimension.Client),
            "reasons" => queries.ReasonBreakdown(window),
            "top" => queries.Top(window, 10),
            "top by series" => queries.Top(window, 10, TopListGrouping.Series),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "That is not one of the shapes this layer offers.")
        };
    }

    private static TimeSpan TimeOf(Action answering)
    {
        var watch = Stopwatch.StartNew();
        answering();
        watch.Stop();

        return watch.Elapsed;
    }

    private static string Said(string what, TimeSpan taken)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} took {1} over {2} plays, against a stated bound of {3}.",
            what,
            taken,
            PlaysInTheStore,
            LongestAnAnswerMayTake);

    private static string Route(string tail, Guid userId)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/{1}",
            userId.ToString("D", CultureInfo.InvariantCulture),
            tail);

    private static PlayRecord APlay(Guid userId, Guid itemId, DateTime startedUtc)
    {
        var watched = TimeSpan.FromMinutes(20);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Episode",
            ParentId = itemId,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    private async Task Within(string what, Func<Task<InProcessEndpoints.Answer>> asking)
    {
        var watch = Stopwatch.StartNew();
        var answer = await asking().ConfigureAwait(true);
        watch.Stop();

        _said.WriteLine(Said(what, watch.Elapsed));

        Assert.Equal(200, answer.Status);
        Assert.True(watch.Elapsed <= LongestAnAnswerMayTake, Said(what, watch.Elapsed));
    }

    private SqlitePlayStore OpenTheStore() => new(_root);

    /// <summary>
    /// Fills the store, spreading the plays over a hundred items so that a top
    /// list has something to order, and giving each item two accounts so that
    /// the shapes carrying the group-size rule answer rather than withhold.
    /// </summary>
    /// <remarks>
    /// The pairing is the part to read rather than the alternation. An earlier
    /// seed alternated the account on every row and cycled the item on every
    /// row too, so an item's index and the account's both followed the row
    /// number and every item ended up with exactly one account behind it. The
    /// top list withheld its answer on that, and the case timed a refusal while
    /// reading as though it had timed a fold. Consecutive rows share an item
    /// here, so the two accounts land on the same one.
    /// </remarks>
    private void Seed(int howMany, Guid theirs)
    {
        var somebodyElse = Caller.SomeoneElse.UserId;
        var items = new Guid[100];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
        }

        var rows = new List<PlayRecord>(howMany);
        for (var i = 0; i < howMany; i++)
        {
            rows.Add(APlay(
                i % 2 == 0 ? theirs : somebodyElse,
                items[(i / 2) % items.Length],
                NewYear + (BetweenPlays * i)));
        }

        using var store = OpenTheStore();

        store.AddMany(rows);
    }

    private IEnumerable<PlayRecord> TwoGoodRowsAndABadOne(Guid theirs)
    {
        var item = new Guid(1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

        yield return APlay(theirs, item, NewYear);
        yield return APlay(theirs, item, NewYear.AddHours(1));

        // A transcode reason carrying the character the store separates reasons
        // with, which the write refuses at the last moment the original is
        // still there to refuse. A row the store will not take, arriving after
        // rows it already has.
        yield return APlay(theirs, item, NewYear.AddHours(2)) with
        {
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = ["one|two"]
            }
        };
    }
}
