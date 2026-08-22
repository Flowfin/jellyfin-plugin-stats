// What a caller is told about the window their wrap-up covers, over the route a
// page would use rather than at the fold.
//
// Issue #69's first condition asks for the statement in the response and not
// only on the page. The fold's own case is in YearCoverageTests and asserts the
// same window on the object; this one is about what survives being serialised
// and handed back, because a property a page never receives is a statement
// nobody made. The two failures it stands against are a partial year presented
// as a whole one, and a figure scaled up to the year on the heading.
//
// Nothing here binds a port or drives a browser. The store is real and sits in
// a temporary directory, the retention sweep is the real one, and the request
// goes through the framework's own routing over an in-memory context.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The covered window as a caller receives it. Issue #69.
/// </summary>
public sealed class YearCoverageInTheResponseTests : IDisposable
{
    /// <summary>
    /// The year every case here is about. It is not a leap year, so a slip of
    /// one day is visible in the count rather than hidden by February.
    /// </summary>
    private const int Year = 2025;

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="YearCoverageInTheResponseTests"/> class.
    /// </summary>
    public YearCoverageInTheResponseTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// A store swept back to September answers a request for that year with a
    /// window that says September, and with the figures the four surviving rows
    /// carry.
    /// </summary>
    /// <returns>The running case.</returns>
    /// <remarks>
    /// The rows are one a month and the cutoff falls exactly on one, so the four
    /// months the answer names are four months rather than an arithmetic
    /// accident. Every figure read out of the body is compared against the rows
    /// that survived and never against the twelve that were written, which is
    /// the second condition of issue #69 asserted on the response: a fold that
    /// scaled anything up to the year on the heading disagrees with the count
    /// taken directly.
    /// </remarks>
    [Fact]
    public async Task ASweptStoreAnswersWithTheWindowItCanStillCoverFor()
    {
        var who = Caller.Someone;
        var survivors = AStoreSweptBackToSeptemberFor(who.UserId);

        Assert.Equal(4, survivors.Count);

        using var endpoints = new InProcessEndpoints(fold: FoldFromTheStore);

        var answer = await endpoints.Get(YearPath(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var document = JsonDocument.Parse(answer.Body);
        var coverage = document.RootElement.GetProperty("coverage");

        Assert.False(coverage.GetProperty("wholeYear").GetBoolean());
        Assert.Equal("2025-09-01", coverage.GetProperty("firstDayCovered").GetString());
        Assert.Equal("2025-12-31", coverage.GetProperty("lastDayCovered").GetString());
        Assert.Equal(30 + 31 + 30 + 31, coverage.GetProperty("daysCovered").GetInt32());
        Assert.Equal("2025-09-01", coverage.GetProperty("earliestPlay").GetString());

        // Nothing is scaled up to the year on the heading. Four rows survived,
        // so the body carries four plays and the watched time those four
        // recorded, and not twelve of either.
        Assert.Equal(4, document.RootElement.GetProperty("plays").GetInt64());
        Assert.Equal(
            survivors.Aggregate(TimeSpan.Zero, (total, play) => total + play.WatchedDuration),
            TimeSpan.Parse(document.RootElement.GetProperty("watched").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A store that never lost a day answers with the whole year, and says so
    /// rather than leaving the window out.
    /// </summary>
    /// <returns>The running case.</returns>
    /// <remarks>
    /// The pair to the case above, and the one that decides whether the
    /// statement is worth anything. A window stated only where the year is
    /// short asks a reader to take an absence for an assurance, so an answer
    /// with nothing missing has to carry the window too.
    /// <para>
    /// The store's oldest row is from the year before on purpose. The window is
    /// the later of the first of January and the day that row started on, and a
    /// window reading the row straight through would open in the previous year
    /// and count more days than the year has. It is also the ordinary case on a
    /// server that has been recording for longer than a year.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AStoreThatLostNothingAnswersWithTheWholeYear()
    {
        var who = Caller.Someone;

        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayStartedAt(new DateTime(Year - 1, 11, 3, 12, 0, 0, DateTimeKind.Utc), who.UserId));

            foreach (var month in EveryMonthOf(Year))
            {
                store.Add(APlayStartedAt(month, who.UserId));
            }
        }

        using var endpoints = new InProcessEndpoints(fold: FoldFromTheStore);

        var answer = await endpoints.Get(YearPath(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var document = JsonDocument.Parse(answer.Body);
        var coverage = document.RootElement.GetProperty("coverage");

        Assert.True(coverage.GetProperty("wholeYear").GetBoolean());
        Assert.Equal("2025-01-01", coverage.GetProperty("firstDayCovered").GetString());
        Assert.Equal(365, coverage.GetProperty("daysCovered").GetInt32());
        Assert.Equal(12, document.RootElement.GetProperty("plays").GetInt64());
    }

    /// <summary>
    /// A store holding nothing answers with no window rather than with a whole
    /// year nobody could have read.
    /// </summary>
    /// <returns>The running case.</returns>
    /// <remarks>
    /// The third state, and the one a page has to be able to tell from the
    /// other two. A year in which somebody watched nothing and a year the store
    /// can no longer answer for are different facts, and an empty window
    /// reported as a full one would make the first unreadable.
    /// </remarks>
    [Fact]
    public async Task AStoreHoldingNothingAnswersWithNoWindowAtAll()
    {
        var who = Caller.Someone;

        using (var store = new SqlitePlayStore(_root))
        {
            Assert.Null(store.OldestPlayStartedUtc());
        }

        using var endpoints = new InProcessEndpoints(fold: FoldFromTheStore);

        var answer = await endpoints.Get(YearPath(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var document = JsonDocument.Parse(answer.Body);
        var coverage = document.RootElement.GetProperty("coverage");

        Assert.False(coverage.GetProperty("wholeYear").GetBoolean());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("firstDayCovered").ValueKind);
        Assert.Equal(0, coverage.GetProperty("daysCovered").GetInt32());
        Assert.Equal(JsonValueKind.Null, coverage.GetProperty("earliestPlay").ValueKind);
        Assert.False(document.RootElement.GetProperty("anythingRecorded").GetBoolean());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// Writes a year of plays a month apart and runs the real retention sweep
    /// with a cutoff four months from the end of it.
    /// </summary>
    /// <param name="userId">Whose plays are written.</param>
    /// <returns>The rows the sweep left behind.</returns>
    private IReadOnlyList<PlayRecord> AStoreSweptBackToSeptemberFor(Guid userId)
    {
        using (var store = new SqlitePlayStore(_root))
        {
            foreach (var month in EveryMonthOf(Year))
            {
                store.Add(APlayStartedAt(month, userId));
            }
        }

        var cutoff = new DateTime(Year, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var deleted = new RetentionSweep(() => new SqlitePlayStore(_root), RetentionSweep.DefaultBite)
            .Run(cutoff, new IgnoredProgress(), CancellationToken.None);

        Assert.Equal(8, deleted);

        using var after = new SqlitePlayStore(_root);

        return after.PlaysFor(userId).ToList();
    }

    /// <summary>
    /// Folds one account's year out of the store on disk, which is what the
    /// plugin's own registration does.
    /// </summary>
    /// <remarks>
    /// The store is opened for the length of the question and closed again. The
    /// oldest start is asked of the same store in the same breath, so the
    /// window the answer states is the window that store could answer for at
    /// the moment it was read.
    /// </remarks>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">Which year.</param>
    /// <param name="zone">The zone its days are read in.</param>
    /// <param name="topCount">How long a top list may be.</param>
    /// <returns>The wrap-up.</returns>
    private YearInReview FoldFromTheStore(Guid userId, int year, TimeZoneInfo zone, int topCount)
    {
        using var store = new SqlitePlayStore(_root);

        return YearInReview.Over(
            store.PlaysFor(userId),
            userId,
            year,
            zone,
            topCount,
            store.OldestPlayStartedUtc());
    }

    /// <summary>
    /// The route a page would ask for, for the year every case here is about.
    /// </summary>
    /// <param name="userId">Whose year.</param>
    /// <returns>The path.</returns>
    private static string YearPath(Guid userId)
        => "/Stats/Users/" + userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture) + "/Years/" + Year.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Noon on the first of each month of a year, in UTC.
    /// </summary>
    /// <param name="year">The year.</param>
    /// <returns>Twelve moments.</returns>
    private static IEnumerable<DateTime> EveryMonthOf(int year)
        => Enumerable.Range(1, 12).Select(month => new DateTime(year, month, 1, 12, 0, 0, DateTimeKind.Utc));

    /// <summary>
    /// One finished play, with every field a value written here.
    /// </summary>
    /// <param name="startedUtc">When it started.</param>
    /// <param name="userId">Whose play it is.</param>
    /// <returns>The row.</returns>
    private static PlayRecord APlayStartedAt(DateTime startedUtc, Guid userId)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.DirectPlay,
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
        };
    }

    /// <summary>
    /// A progress reporter for the cases that are not about progress.
    /// </summary>
    private sealed class IgnoredProgress : IProgress<double>
    {
        /// <inheritdoc />
        public void Report(double value)
        {
        }
    }
}
