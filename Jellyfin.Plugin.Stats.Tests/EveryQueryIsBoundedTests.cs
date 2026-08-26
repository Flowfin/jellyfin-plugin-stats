// What a request too large to answer honestly gets.
//
// The two caps are the plugin's and never the caller's: how long a range may be,
// and how many plays it may hold. Both are refused rather than trimmed, because
// a report folded from the part of a range that fitted reads exactly like a
// report over the whole of it, and nothing downstream can tell the two apart.
//
// A real store rather than a fake, for the same reason the query cases beside
// this file use one: what is asserted here is a property of the read and not of
// the fold. Every moment is chosen by the test and nothing reads a clock.
//
// Issue #56, first condition. The third asks for a case over a large store and
// there is none here; what was tried, what it cost and what it broke are written
// on that issue.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class EveryQueryIsBoundedTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime NewYear = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public EveryQueryIsBoundedTests()
    {
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
    /// A range longer than the cap is refused, and the refusal says what the cap
    /// is.
    /// </summary>
    /// <remarks>
    /// Both halves are asserted. A refusal that did not name the number leaves
    /// the caller guessing at what would have been accepted, and guessing is
    /// done by halving the range until something comes back, which is the same
    /// arbitrary work this cap exists to stop.
    /// </remarks>
    [Fact]
    public void ARangeLongerThanTheCapIsRefusedAndSaysWhatTheCapIs()
    {
        var tooLong = NewYear + QueryWindow.LongestRangeAnyShapeAnswers + TimeSpan.FromSeconds(1);

        var refused = Assert.Throws<ArgumentException>(() => QueryWindow.Of(NewYear, tooLong));

        Assert.Contains("367", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A range exactly at the cap is accepted.
    /// </summary>
    /// <remarks>
    /// The other side of the boundary, and the one a cap gets wrong quietly. A
    /// comparison one step out refuses the longest range this plugin is meant to
    /// offer, and the report that stops being available is the calendar year the
    /// cap was sized for.
    /// </remarks>
    [Fact]
    public void ARangeExactlyAtTheCapIsAccepted()
    {
        var window = QueryWindow.Of(NewYear, NewYear + QueryWindow.LongestRangeAnyShapeAnswers);

        Assert.Equal(QueryWindow.LongestRangeAnyShapeAnswers, window.ToUtc - window.FromUtc);
    }

    /// <summary>
    /// A leap year read in a zone with summer time is exactly 366 days and fits
    /// inside the cap.
    /// </summary>
    /// <remarks>
    /// The report the cap was sized for, measured rather than argued, and it
    /// measures the opposite of what a reader expects. Summer time is not why
    /// the cap carries a day of slack: a zone that puts its clocks forward in
    /// the spring puts them back in the autumn, so the two cancel inside one
    /// calendar year and 2028 in Berlin is 366 days to the second. This case was
    /// written asserting that it was longer than that, and the run said
    /// otherwise, which is why the reasoning at the constant says what it says
    /// now.
    /// </remarks>
    [Fact]
    public void ALeapYearInAZoneWithSummerTimeIsExactlyThreeHundredAndSixtySixDays()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "W. Europe Standard Time" : "Europe/Berlin");

        var from = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), zone);
        var to = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2029, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), zone);

        Assert.Equal(TimeSpan.FromDays(366), to - from);

        var window = QueryWindow.Of(from, to);

        Assert.Equal(to - from, window.ToUtc - window.FromUtc);
    }

    /// <summary>
    /// A range a little longer than a leap year is still accepted.
    /// </summary>
    /// <remarks>
    /// What the slack in the cap is actually for. A zone that changes its
    /// standard offset partway through a year, which is a political decision
    /// rather than a seasonal rule, makes the interval between two local
    /// midnights a year apart longer than the calendar count of its days. A cap
    /// sitting exactly on 366 days would refuse a calendar year in such a zone,
    /// and that is the one report this plugin is most likely to be asked for.
    /// </remarks>
    [Fact]
    public void ARangeALittleLongerThanALeapYearIsStillAccepted()
    {
        var window = QueryWindow.Of(NewYear, NewYear.AddDays(366).AddHours(1));

        Assert.Equal(TimeSpan.FromDays(366) + TimeSpan.FromHours(1), window.ToUtc - window.FromUtc);
    }

    /// <summary>
    /// Every shape refuses a range that holds more plays than its bound allows,
    /// and the refusal names the bound.
    /// </summary>
    /// <remarks>
    /// Once per shape rather than once. The bound is applied in one place today,
    /// and a shape added later that reached the store some other way would walk
    /// through a case that only asked the first of them.
    /// </remarks>
    /// <param name="shape">Which shape to ask.</param>
    [Theory]
    [InlineData("total")]
    [InlineData("series")]
    [InlineData("distribution")]
    [InlineData("breakdown")]
    [InlineData("reasons")]
    [InlineData("top")]
    public void EveryShapeRefusesARangeHoldingMorePlaysThanTheBoundAllows(string shape)
    {
        Seed(_root, 12, NewYear, TimeSpan.FromMinutes(1));

        var queries = new AggregateQueries(OpenTheStore);
        var window = QueryWindow.Of(NewYear, NewYear.AddDays(1), mostPlays: 11);

        var refused = Assert.Throws<TooManyPlaysToAnswerException>(() => Ask(queries, shape, window));

        Assert.Equal(11, refused.MostPlays);
        Assert.Contains("11", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A range holding exactly the bound is answered rather than refused.
    /// </summary>
    /// <remarks>
    /// The boundary the refusal is most likely to be one out on, and being one
    /// out here is worse than having no refusal at all: every report over a
    /// range that happened to sit on the bound would fail, and the caller would
    /// be told to ask for less when there was nothing left to leave out.
    /// </remarks>
    [Fact]
    public void ARangeHoldingExactlyTheBoundIsAnswered()
    {
        Seed(_root, 11, NewYear, TimeSpan.FromMinutes(1));

        var totals = new AggregateQueries(OpenTheStore)
            .Total(QueryWindow.Of(NewYear, NewYear.AddDays(1), mostPlays: 11));

        Assert.Equal(11, totals.Plays);
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
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "That is not one of the shapes this layer offers.")
        };
    }

    private static PlayRecord APlay(Guid userId, DateTime startedUtc)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = AFilm,
            ItemType = "Movie",
            ParentId = null,
            ItemName = "A Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(30),
            WatchedDuration = TimeSpan.FromMinutes(30),
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

    private SqlitePlayStore OpenTheStore() => new(_root);

    /// <summary>
    /// Writes plays into the store, one every <paramref name="apart"/> from
    /// <paramref name="firstUtc"/>, alternating between two accounts.
    /// </summary>
    /// <remarks>
    /// Two accounts rather than one, so that the breakdown shape has enough
    /// behind each of its rows to be answered at all and the timed case measures
    /// a fold rather than a refusal.
    /// </remarks>
    /// <param name="howMany">How many rows.</param>
    /// <param name="firstUtc">When the first one started.</param>
    /// <param name="apart">The gap between two.</param>
    private static void Seed(string root, int howMany, DateTime firstUtc, TimeSpan apart)
    {
        using var store = new SqlitePlayStore(root);

        for (var i = 0; i < howMany; i++)
        {
            store.Add(APlay(i % 2 == 0 ? Alice : Bob, firstUtc + (apart * i)));
        }
    }
}
