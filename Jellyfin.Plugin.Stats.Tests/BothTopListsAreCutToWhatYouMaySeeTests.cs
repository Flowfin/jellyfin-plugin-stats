// One visibility rule over both top lists an account can be shown about itself.
//
// Issue #54 decided that a report does not name an item the reader may not see,
// and the year route carried it. The statistics route was built afterwards and
// did not: it folded a top list off the play rows and named whatever it found,
// so one account asking two routes about the same rows got two answers that
// disagreed about the same item. Issue #299 settled it in favour of the year,
// on the ground that an item can be moved out of a library an account no longer
// has access to and a top list would then name it back to them.
//
// So the assertion here is an agreement rather than a behaviour: the same
// account, the same rows, the same library, and the same answer about one item
// from both routes. A case that drove only the statistics route would pass on
// the day somebody weakened the year's rule instead.
//
// Both routes run through the in-process pipeline, so what is read is the body
// a caller receives. The statistics route answers from a real store on disk,
// because its fold reads rollups and rows through the store rather than from a
// list a test hands it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What each of the two personal top lists names once the library has been
/// asked.
/// </summary>
public sealed class BothTopListsAreCutToWhatYouMaySeeTests : IDisposable
{
    private static readonly Guid Withheld = Guid.Parse("c0ffee00-0000-0000-0000-00000000000a");

    private static readonly Guid Deleted = Guid.Parse("c0ffee00-0000-0000-0000-00000000000b");

    private static readonly Guid Shown = Guid.Parse("c0ffee00-0000-0000-0000-00000000000c");

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// <summary>
    /// A day inside the thirty the statistics window covers, read back from the
    /// harness's fixed clock in June 2026.
    /// </summary>
    private static readonly DateTime InTheWindow = new(2026, 5, 20, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="BothTopListsAreCutToWhatYouMaySeeTests"/> class.
    /// </summary>
    public BothTopListsAreCutToWhatYouMaySeeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
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
    /// The two routes give the same answer about the same item for the same
    /// account: an item they may not see is named by neither, and an item they
    /// may is named by both.
    /// </summary>
    /// <remarks>
    /// The condition issue #299 is closed on, and it is stated as an agreement
    /// on purpose. Either rule satisfies it as long as one rule holds over both
    /// lists, which is what that issue asked to settle; which of the two it was
    /// settled in favour of is the case below.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task NeitherRouteNamesAnItemTheReaderMayNotSee()
    {
        var who = Caller.Someone;
        var library = FakeItemAccess.EverythingVisible
            .Withholding(who.UserId, Withheld)
            .LetGoOf(Deleted);

        var fromTheStatistics = await NamedByTheStatistics(who, library);
        var fromTheYear = await NamedByTheYear(who, library);

        Assert.DoesNotContain("Withheld since", fromTheStatistics);
        Assert.DoesNotContain("Withheld since", fromTheYear);

        Assert.Contains("Still shown", fromTheStatistics);
        Assert.Contains("Still shown", fromTheYear);

        // An item the library has let go of is not an item this account may not
        // see. It is a play of something since deleted, and both routes name it
        // off the row the way every other label is named.
        Assert.Contains("Deleted since", fromTheStatistics);
        Assert.Contains("Deleted since", fromTheYear);
    }

    /// <summary>
    /// With a library that withholds nothing, both routes name all three. This
    /// is the other direction of the case above.
    /// </summary>
    /// <remarks>
    /// Without it, a route that answered with an empty list whatever it was
    /// handed would satisfy the agreement and satisfy this file, which is the
    /// shape a guard has to be built against rather than around.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task BothRoutesNameEverythingWhereTheLibraryWithholdsNothing()
    {
        var who = Caller.Someone;
        var library = FakeItemAccess.EverythingVisible;

        var fromTheStatistics = await NamedByTheStatistics(who, library);
        var fromTheYear = await NamedByTheYear(who, library);

        Assert.Contains("Withheld since", fromTheStatistics);
        Assert.Contains("Withheld since", fromTheYear);
    }

    /// <summary>
    /// The statistics response still counts a play of an item it may not name.
    /// </summary>
    /// <remarks>
    /// The sentence issue #54 is written on, held here for the route that did
    /// not have it. Dropping the play from the totals as well would answer a
    /// different question, and the difference between a total and a list is not
    /// something an account can turn back into a name.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task WhatIsNotNamedIsStillCounted()
    {
        var who = Caller.Someone;
        var library = FakeItemAccess.EverythingVisible.Withholding(who.UserId, Withheld);

        Seed(ThreePlaysBy(who.UserId));

        using var endpoints = OverTheStore(library);

        var answer = await endpoints.Get(Statistics(who.UserId), who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);

        Assert.Equal(3, body.RootElement.GetProperty("plays").GetInt64());
        Assert.Equal(2, body.RootElement.GetProperty("topItems").GetArrayLength());
    }

    /// <summary>
    /// A withheld item does not shorten the list. The next item takes its place.
    /// </summary>
    /// <remarks>
    /// The cut is taken after the question rather than before it, which is the
    /// year's own shape. Filtering a list already cut to its length would leave
    /// a person with one hidden item seeing four titles where somebody else
    /// sees five, and the missing row would be a fact about the library they
    /// could read off the length alone.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AWithheldItemDoesNotShortenTheList()
    {
        var who = Caller.Someone;
        var sixItems = new List<PlayRecord>();

        for (var i = 0; i < 6; i++)
        {
            sixItems.Add(APlay(
                who.UserId,
                new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
                string.Format(CultureInfo.InvariantCulture, "Item {0}", i),

                // Descending, so the ranking is the order they were built in
                // and the withheld one is the longest watched rather than the
                // one the cut would have dropped anyway.
                TimeSpan.FromMinutes(60 - (i * 5))));
        }

        Seed(sixItems);

        var everything = await NamesInTopList(FakeItemAccess.EverythingVisible, who);
        var oneHidden = await NamesInTopList(
            FakeItemAccess.EverythingVisible.Withholding(who.UserId, new Guid(1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0])),
            who);

        Assert.Equal(YourStatisticsController.TopListLength, everything.Count);
        Assert.Equal(YourStatisticsController.TopListLength, oneHidden.Count);

        Assert.DoesNotContain("Item 0", oneHidden);
        Assert.Contains("Item 5", oneHidden);
        Assert.DoesNotContain("Item 5", everything);
    }

    private async Task<List<string>> NamedByTheStatistics(Caller who, FakeItemAccess library)
    {
        Seed(ThreePlaysBy(who.UserId));

        using var endpoints = OverTheStore(library);

        var answer = await endpoints.Get(Statistics(who.UserId), who);

        Assert.Equal(200, answer.Status);

        return NamesIn(answer.Body, "topItems");
    }

    private async Task<List<string>> NamedByTheYear(Caller who, FakeItemAccess library)
    {
        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) => YearInReview.Over(
                ThreePlaysBy(userId),
                userId,
                year,
                zone,
                topCount,
                oldestPlayStartedUtc: null),
            access: library);

        var answer = await endpoints.Get(Year(who.UserId), who);

        Assert.Equal(200, answer.Status);

        return NamesIn(answer.Body, "topItems");
    }

    private async Task<List<string>> NamesInTopList(FakeItemAccess library, Caller who)
    {
        using var endpoints = OverTheStore(library);

        var answer = await endpoints.Get(Statistics(who.UserId), who);

        Assert.Equal(200, answer.Status);

        return NamesIn(answer.Body, "topItems");
    }

    private InProcessEndpoints OverTheStore(FakeItemAccess library)
        => new(
            access: library,
            reports: new AggregateQueries(() => new SqlitePlayStore(_root, Utc)));

    private void Seed(IReadOnlyList<PlayRecord> plays)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Utc);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }

    private static List<string> NamesIn(string body, string property)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement
            .GetProperty(property)
            .EnumerateArray()
            .Select(row => row.GetProperty("name").GetString() ?? string.Empty)
            .ToList();
    }

    private static string Statistics(Guid userId)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/Statistics/last30Days",
            userId.ToString("D", CultureInfo.InvariantCulture));

    private static string Year(Guid userId)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/Stats/Users/{0}/Years/2026",
            userId.ToString("D", CultureInfo.InvariantCulture));

    private static List<PlayRecord> ThreePlaysBy(Guid userId) =>
    [
        APlay(userId, Withheld, "Withheld since", TimeSpan.FromMinutes(90)),
        APlay(userId, Deleted, "Deleted since", TimeSpan.FromMinutes(60)),
        APlay(userId, Shown, "Still shown", TimeSpan.FromMinutes(30))
    ];

    private static PlayRecord APlay(Guid userId, Guid itemId, string itemName, TimeSpan watched)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Movie",
            ParentId = null,
            ItemName = itemName,
            ItemRuntime = TimeSpan.FromMinutes(120),
            ChannelName = null,
            StartedUtc = InTheWindow,
            EndedUtc = InTheWindow.Add(watched),
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
}
