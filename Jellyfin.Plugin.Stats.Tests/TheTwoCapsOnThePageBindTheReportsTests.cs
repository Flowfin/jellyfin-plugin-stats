// The two caps on the settings page decide what the report routes answer.
//
// Issue #305: MaximumRangeDays and MaximumRowsPerResponse were on the page,
// validated and stored, and reached nothing. What bounded the reports was two
// constants in the query layer that nothing on the page could move, so an
// operator who lowered either was told nothing and got the same answer as
// before.
//
// Every case here changes one setting and reads the answer, in both directions:
// the request that is answered under one value and refused under another. A
// case that only asserted the refusal would pass over a route that refused
// everything, and one that only asserted the answer would pass over a route
// that ignored the setting entirely.
//
// The endpoints run in this process over the in-memory transport, so nothing
// binds a port and nothing needs a server.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What the two caps an operator can set do to a report.
/// </summary>
public class TheTwoCapsOnThePageBindTheReportsTests
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The shipped default is the plugin's own longest range rather than a
    /// second number beside it.
    /// </summary>
    /// <remarks>
    /// The two were 400 and 367 while the setting reached nothing, which cost
    /// nobody anything because only one of them decided a request. Now that the
    /// setting decides, a default wider than the query layer's own ceiling
    /// would widen every installation by five weeks on the day this landed, for
    /// no reason anybody chose. They are held equal here so neither can be
    /// edited alone.
    /// </remarks>
    [Fact]
    public void TheDefaultRangeCapIsThePluginsOwnLongestRange()
    {
        Assert.Equal(
            QueryWindow.LongestRangeAnyShapeAnswers,
            TimeSpan.FromDays(ConfigurationLimits.DefaultMaximumRangeDays));
    }

    /// <summary>
    /// A ceiling of no time at all is refused where it is named, rather than
    /// becoming a window that answers nothing.
    /// </summary>
    /// <remarks>
    /// The configuration cannot produce one - a day count below one is refused
    /// and falls back to the default - so this guard exists for the other
    /// callers of a public method. A zero ceiling refuses every range including
    /// the empty one, which is a report route that answers nothing and says the
    /// range was too long.
    /// </remarks>
    [Fact]
    public void ACeilingOfNoTimeAtAllIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => QueryWindow.Of(March, March.AddDays(1), longestRange: TimeSpan.Zero));

        Assert.NotNull(QueryWindow.Of(March, March.AddDays(1), longestRange: TimeSpan.FromDays(1)));
    }

    /// <summary>
    /// A range longer than the cap on the page is refused, and the same range is
    /// answered where the cap allows it.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRangeCapRefusesARangeLongerThanItAllows()
    {
        var thirtyDays = Usage(March, March.AddDays(30));

        Assert.Equal(400, await StatusOf(thirtyDays, new PluginConfiguration { MaximumRangeDays = 7 }));
        Assert.Equal(200, await StatusOf(thirtyDays, new PluginConfiguration { MaximumRangeDays = 30 }));
    }

    /// <summary>
    /// A range the shipped default refuses is answered where an operator has
    /// raised the cap.
    /// </summary>
    /// <remarks>
    /// The direction that makes this a cap rather than a suggestion, and the
    /// one whose cost is named in the change that landed it: the ceiling on
    /// this route is the operator's number, so it moves in both directions
    /// inside what the configuration accepts. What is not settable, and is the
    /// bound that stops a request making the server do arbitrary work, is the
    /// number of plays any shape will read.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRangeCapAnswersARangeTheDefaultRefuses()
    {
        var fourHundredDays = Usage(March, March.AddDays(400));

        Assert.Equal(400, await StatusOf(fourHundredDays, new PluginConfiguration()));
        Assert.Equal(200, await StatusOf(fourHundredDays, new PluginConfiguration { MaximumRangeDays = 500 }));
    }

    /// <summary>
    /// A usage answer carrying more days than the row cap allows is refused, and
    /// the same request is answered where the cap allows it.
    /// </summary>
    /// <remarks>
    /// One row per day that had plays, so four days are four rows. A cap of
    /// three is an operator saying no response may carry more than three, and
    /// what they get is a refusal rather than a week cut to its first three
    /// days - which would read exactly like a week with three days in it.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRowCapRefusesAUsageAnswerWithMoreDaysThanItAllows()
    {
        var aWeek = Usage(March, March.AddDays(7));

        Assert.Equal(400, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 3 }));
        Assert.Equal(200, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 4 }));
    }

    /// <summary>
    /// A breakdown carrying more members than the row cap allows is refused, and
    /// the same request is answered where the cap allows it.
    /// </summary>
    /// <remarks>
    /// The shape the cap was put on the page for. A server with hundreds of
    /// clients answers one request with a row for each of them, and the number
    /// of rows follows what the server has rather than anything the request
    /// says.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRowCapRefusesABreakdownWithMoreMembersThanItAllows()
    {
        var aWeek = Breakdown(March, March.AddDays(7));

        Assert.Equal(400, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 3 }));
        Assert.Equal(200, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 4 }));
    }

    /// <summary>
    /// A top list longer than the row cap allows is refused, and the same
    /// request is answered where the cap allows it.
    /// </summary>
    /// <remarks>
    /// The cap reaches every shape on this route rather than the two whose row
    /// count follows the data. A cap that reached two of three responses would
    /// be the same defect one step smaller, and an operator reading the page
    /// has no way to know which shapes a partial cap covers.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheRowCapRefusesATopListLongerThanItAllows()
    {
        var aWeek = Top(March, March.AddDays(7));

        Assert.Equal(400, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 3 }));
        Assert.Equal(200, await StatusOf(aWeek, new PluginConfiguration { MaximumRowsPerResponse = 4 }));
    }

    /// <summary>
    /// A cap changed between two requests decides the second one, with no
    /// restart between them.
    /// </summary>
    /// <remarks>
    /// Both properties declare that a change takes effect at once. A consumer
    /// that read either number when it was built would answer the second
    /// request under the first request's cap, the page would have saved
    /// cleanly, and nothing anywhere would say the two disagree.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task ACapChangedBetweenTwoRequestsDecidesTheSecond()
    {
        var settings = new PluginConfiguration { MaximumRowsPerResponse = 4 };

        using var endpoints = new InProcessEndpoints(
            configuration: settings,
            reports: new AggregateQueries(() => new PlaysHeldInMemory(AWeekOfPlays())));

        var aWeek = Usage(March, March.AddDays(7));

        Assert.Equal(200, (await endpoints.Get(aWeek, Caller.Administrator)).Status);

        // The page saving a lower cap, and nothing else. No restart, no new
        // container, no second harness.
        settings.MaximumRowsPerResponse = 3;

        Assert.Equal(400, (await endpoints.Get(aWeek, Caller.Administrator)).Status);
    }

    private static async Task<int> StatusOf(string path, PluginConfiguration settings)
    {
        using var endpoints = new InProcessEndpoints(
            configuration: settings,
            reports: new AggregateQueries(() => new PlaysHeldInMemory(AWeekOfPlays())));

        return (await endpoints.Get(path, Caller.Administrator)).Status;
    }

    private static string Usage(DateTime from, DateTime to)
        => "/Stats/Reports/Usage" + Range(from, to);

    private static string Breakdown(DateTime from, DateTime to)
        => "/Stats/Reports/Breakdown" + Range(from, to);

    private static string Top(DateTime from, DateTime to)
        => "/Stats/Reports/Top" + Range(from, to);

    private static string Range(DateTime from, DateTime to)
        => string.Format(
            CultureInfo.InvariantCulture,
            "?from={0}&to={1}",
            from.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

    /// <summary>
    /// Four items on four clients over four days, each played by both accounts.
    /// </summary>
    /// <remarks>
    /// Two accounts behind every row, because a row standing on one account is
    /// withheld and a withheld answer carries no rows to cap. Four of each, so
    /// every one of the three shapes answers with four rows and a cap of three
    /// refuses while a cap of four does not - which is the smallest pair that
    /// tells a cap from a route that refuses everything, and one set of plays
    /// rather than three.
    /// </remarks>
    /// <returns>The plays.</returns>
    private static IReadOnlyList<PlayRecord> AWeekOfPlays()
    {
        var plays = new List<PlayRecord>();

        for (var i = 0; i < 4; i++)
        {
            var item = new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);
            var client = string.Format(CultureInfo.InvariantCulture, "Client {0}", i);

            plays.Add(APlay(Alice, March.AddDays(i), item, client));
            plays.Add(APlay(Bob, March.AddDays(i).AddMinutes(90), item, client));
        }

        return plays;
    }

    private static PlayRecord APlay(Guid userId, DateTime startedUtc, Guid itemId, string clientName)
    {
        var length = TimeSpan.FromMinutes(40);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Movie",
            ParentId = null,
            ItemName = "A Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc + length,
            WatchedDuration = length,
            ReachedTheEnd = false,
            ClientName = clientName,
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
        };
    }
}
