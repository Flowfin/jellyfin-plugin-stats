// Which of a wrap-up's figures came from the day-by-day aggregates and which
// from the play rows, over the route a page would use rather than at the fold.
//
// The second condition of issue #254 asks for the statement in the RESPONSE and
// not on the object. The fold's own cases are in AYearReadFromRollupsTests; this
// one is about what survives being serialised and handed back, because a
// property a page never receives is a statement nobody made.
//
// The failure it stands against is a reader given one set of figures and one
// window. YearCoverage says what the store lost and it is one window, but a
// figure read out of an aggregate that outlived its rows is covered further back
// than that window says, so a reader with the window alone cannot tell which of
// the figures the raw rows still support.
//
// Nothing here binds a port or drives a browser. The store is real and sits in a
// temporary directory, the retention deletion is the real one, and the request
// goes through the framework's own routing over an in-memory context.

using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class WhichFiguresCameFromWhichSourceTests : IDisposable
{
    private const int Year = 2026;

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly string _root;

    public WhichFiguresCameFromWhichSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// A wrap-up drawn from both sources says so on the response: the totals off
    /// the aggregates, the item figures off the rows, and nothing left for a
    /// reader to work out.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AWrapUpDrawnFromBothSourcesSaysWhichFiguresCameFromWhich()
    {
        var who = Caller.Someone;

        Seed(who.UserId);

        using var endpoints = new InProcessEndpoints(fold: FoldFromTheStore);

        var answer = await endpoints.Get(YearPath(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var document = JsonDocument.Parse(answer.Body);
        var sources = document.RootElement.GetProperty("sources");

        Assert.Equal("aggregates", sources.GetProperty("totals").GetString());
        Assert.Equal("plays", sources.GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Null, sources.GetProperty("notComputedBecause").ValueKind);

        // Both groups of figures are there, which is what makes the statement
        // above a statement about two sources rather than about one.
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("plays").ValueKind);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("distinctItems").ValueKind);
    }

    /// <summary>
    /// The same wrap-up after the rows behind it have been swept. The totals are
    /// still answered and the response says the item figures could not be taken,
    /// with the reason, rather than leaving them absent for a reader to explain
    /// to themselves.
    /// </summary>
    /// <returns>The running case.</returns>
    /// <remarks>
    /// This is the third condition of issue #254 met on the response as well as
    /// on the fold, and it is the state a retention sweep produces on purpose:
    /// issue #253 settled that a retention deletion leaves the rollups it aged
    /// out of standing.
    /// </remarks>
    [Fact]
    public async Task AWrapUpWhoseRowsAreGoneStillAnswersAndSaysWhatCouldNotBeTaken()
    {
        var who = Caller.Someone;

        Seed(who.UserId);

        using (var sweeping = new SqlitePlayStore(_root, Utc))
        {
            sweeping.DeletePlaysStartedBefore(
                new DateTime(Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DeletionClass.Retention,
                limit: 10_000);
        }

        using var endpoints = new InProcessEndpoints(fold: FoldFromTheStore);

        var answer = await endpoints.Get(YearPath(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var document = JsonDocument.Parse(answer.Body);
        var sources = document.RootElement.GetProperty("sources");

        Assert.Equal("aggregates", sources.GetProperty("totals").GetString());
        Assert.Equal("not computed", sources.GetProperty("detail").GetString());
        Assert.Contains("no longer in the store", sources.GetProperty("notComputedBecause").GetString());

        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("plays").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("distinctItems").ValueKind);
    }

    private static string YearPath(Guid userId)
        => "/Stats/Users/"
            + userId.ToString("D", CultureInfo.InvariantCulture)
            + "/Years/"
            + Year.ToString(CultureInfo.InvariantCulture);

    private static PlayRecord APlayStartedAt(DateTime startedUtc, Guid userId, int item)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = new Guid($"6666666666666666666666666666666{item}"),
            ItemType = "Episode",
            ParentId = new Guid("77777777777777777777777777777777"),
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(20),
            WatchedDuration = TimeSpan.FromMinutes(20),
            ReachedTheEnd = true,
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
                Reasons = Array.Empty<string>(),
            },
        };

    private void Seed(Guid userId)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Utc);

        for (var month = 1; month <= 3; month++)
        {
            store.Add(APlayStartedAt(new DateTime(Year, month, 4, 12, 0, 0, DateTimeKind.Utc), userId, month));
        }
    }

    private YearInReview FoldFromTheStore(Guid userId, int year, TimeZoneInfo zone, int topCount)
    {
        using var store = new SqlitePlayStore(_root, Utc);

        return AggregateQueries.AYearOver(store, userId, year, zone, topCount);
    }
}
