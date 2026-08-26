// The five shapes every aggregate report is answered through, driven over a
// real store.
//
// A real file rather than a fake, because two of the properties here are about
// the read itself: that a window is half open at both ends, and that a bound is
// a bound. A fake answering from a list would prove the folding and say nothing
// about the statement that fetched the rows.
//
// Every moment is chosen by the test. Nothing here reads a clock, and the
// consent register is handed one that does not move, so a record's moments are
// values rather than whenever the suite ran.
//
// Issue #51, and issue #41 for the rule that withholds a breakdown.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class AggregateQueryTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly Guid AnotherFilm = Guid.Parse("22222222-3333-4444-5555-666666666666");

    private static readonly DateTime March = new(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset WhenTheAnswerWasGiven = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root;

    public AggregateQueryTests()
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
    /// Each of the five shapes answers exactly the same whether the accounts
    /// behind the plays have agreed to be named, withdrawn, or never been asked.
    /// </summary>
    /// <remarks>
    /// The rule proved once per shape rather than once per report, which is what
    /// the third condition of issue #51 asks for. What makes it hold is that no
    /// shape can name an account, so there is nothing for consent to widen, and
    /// the case that keeps it that way is the one below over the dimensions.
    /// <para>
    /// Both directions are recorded, not only the agreement. An answer that grew
    /// under consent and an answer that shrank under a withdrawal are the same
    /// defect seen from two sides, and a case recording only one of them passes
    /// on the half that does not hold.
    /// </para>
    /// </remarks>
    /// <param name="shape">Which of the five to ask.</param>
    [Theory]
    [InlineData("total")]
    [InlineData("series")]
    [InlineData("distribution")]
    [InlineData("breakdown")]
    [InlineData("reasons")]
    [InlineData("top")]
    public void EveryShapeAnswersTheSameWhateverAnAccountHasSaidAboutBeingNamed(string shape)
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web"),
            APlay(Bob, AFilm, March.AddHours(2), "Jellyfin Web"),
            APlay(Alice, AnotherFilm, March.AddDays(1), "Jellyfin Web"),
            APlay(Bob, AnotherFilm, March.AddDays(2), "Jellyfin Web"));

        var queries = new AggregateQueries(OpenTheStore);
        var beforeAnybodyWasAsked = Answer(queries, shape);

        var register = new ConsentRegister(OpenTheStore, new FixedClock(WhenTheAnswerWasGiven));
        register.Agree(Alice, ConsentWording.Version);
        register.Agree(Bob, ConsentWording.Version);
        register.Withdraw(Bob);

        Assert.NotNull(register.For(Alice));
        Assert.NotNull(register.For(Bob));

        Assert.Equal(beforeAnybodyWasAsked, Answer(queries, shape), StringComparer.Ordinal);
    }

    /// <summary>
    /// The things a breakdown may group by are a closed set with no account in
    /// it.
    /// </summary>
    /// <remarks>
    /// This is what the case above rests on, so it is asserted rather than
    /// assumed. A member added to that enumeration is how the consent rule stops
    /// being held by construction, and the day somebody adds one this case goes
    /// red in the file where the rule is written rather than nowhere.
    /// </remarks>
    [Fact]
    public void ABreakdownMayNotBeAskedToGroupByAnAccount()
    {
        Assert.Equal(
            new[] { PlayDimension.Client, PlayDimension.Device },
            Enum.GetValues<PlayDimension>());
    }

    /// <summary>
    /// A breakdown whose every row stands on enough accounts is answered.
    /// </summary>
    [Fact]
    public void ABreakdownEveryRowOfWhichStandsOnEnoughAccountsIsAnswered()
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web"),
            APlay(Bob, AFilm, March.AddHours(1), "Jellyfin Web"),
            APlay(Alice, AnotherFilm, March.AddHours(2), "Jellyfin Web"),
            APlay(Bob, AnotherFilm, March.AddHours(3), "Jellyfin Web"));

        var breakdown = new AggregateQueries(OpenTheStore).Breakdown(AWeekFrom(March), PlayDimension.Client);

        Assert.NotNull(breakdown);
        Assert.Equal(4, breakdown.Plays);
        Assert.Equal("Jellyfin Web", Assert.Single(breakdown.Rows).Key);
    }

    /// <summary>
    /// A breakdown with a row standing on one account is withheld whole, and the
    /// total beside it is still answered.
    /// </summary>
    /// <remarks>
    /// Whole and not row by row. Suppressing the thin row alone leaves the total
    /// beside the rows that remain, and the account that was suppressed is what
    /// the difference between them comes to, so the arithmetic moves rather than
    /// stopping. That reading is issue #41's and this is it as a case.
    /// <para>
    /// The total stays because a total on its own is not half of a subtraction.
    /// Withholding it as well would cost every report on the server a figure
    /// that names nobody.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("client")]
    [InlineData("device")]
    public void ABreakdownWithARowStandingOnOneAccountIsWithheldAndTheTotalIsNot(string dimension)
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web", "device-1"),
            APlay(Alice, AnotherFilm, March.AddHours(1), "Jellyfin Web", "device-1"),
            APlay(Bob, AFilm, March.AddHours(2), "Roku", "device-2"));

        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        Assert.Null(queries.Breakdown(
            window,
            dimension == "client" ? PlayDimension.Client : PlayDimension.Device));

        Assert.Equal(3, queries.Total(window).Plays);
    }

    /// <summary>
    /// On a server of two accounts, nothing this layer answers lets one
    /// account's total be derived by subtraction.
    /// </summary>
    /// <remarks>
    /// The test issue #41's second condition asks for, tried rather than
    /// described, and tried against the total as well as against the rows
    /// because that is where the earlier shape of this rule leaked.
    /// <para>
    /// Two accounts, one of which has agreed to be named. Every route to a
    /// figure about one of them is walked: there is no dimension to group
    /// accounts by, both breakdowns that could stand in for one are withheld,
    /// and the only figure left is a total over both. The subtraction has no
    /// second operand.
    /// </para>
    /// </remarks>
    [Fact]
    public void OneAccountsTotalCannotBeDerivedBySubtractionOnATwoAccountServer()
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web", "device-1", TimeSpan.FromMinutes(100)),
            APlay(Bob, AnotherFilm, March.AddHours(2), "Roku", "device-2", TimeSpan.FromMinutes(150)));

        new ConsentRegister(OpenTheStore, new FixedClock(WhenTheAnswerWasGiven))
            .Agree(Alice, ConsentWording.Version);

        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        Assert.DoesNotContain(
            Enum.GetValues<PlayDimension>(),
            dimension => dimension.ToString().Contains("User", StringComparison.OrdinalIgnoreCase));

        Assert.Null(queries.Breakdown(window, PlayDimension.Client));
        Assert.Null(queries.Breakdown(window, PlayDimension.Device));

        var total = queries.Total(window);

        Assert.Equal(2, total.Plays);
        Assert.Equal(TimeSpan.FromMinutes(250), total.Watched);

        // THIS COMMENT SAID THE TOP LIST WAS THE ONE SHAPE THAT STILL ANSWERED
        // HERE, BECAUSE AN ITEM IS NOT AN ACCOUNT. It is withheld too. Two
        // films, one account behind each, is one row per person under the name
        // of what they watched, and the subtraction this case is about works on
        // that list exactly as it works on a breakdown. Issue #52.
        Assert.Null(queries.Top(window, 10));
    }

    /// <summary>
    /// What stands behind a row is counted in accounts and not in plays.
    /// </summary>
    /// <remarks>
    /// The case that separates the two, and the one the cases above cannot: two
    /// accounts, each watching twice on a client of its own. Counted in plays,
    /// every row stands on two and the breakdown is answered, which hands an
    /// administrator one row per person under another name. Counted in accounts,
    /// every row stands on one and the breakdown is withheld.
    /// <para>
    /// Four hundred plays from one person are still one account, which is the
    /// sentence this asserts. It matters most on the dimensions that are not the
    /// account, which is every dimension there is here.
    /// </para>
    /// </remarks>
    [Fact]
    public void ARowIsCountedByTheAccountsBehindItAndNotByThePlays()
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web", "device-1"),
            APlay(Alice, AnotherFilm, March.AddHours(1), "Jellyfin Web", "device-1"),
            APlay(Bob, AFilm, March.AddHours(2), "Roku", "device-2"),
            APlay(Bob, AnotherFilm, March.AddHours(3), "Roku", "device-2"));

        var queries = new AggregateQueries(OpenTheStore);
        var window = AWeekFrom(March);

        Assert.Null(queries.Breakdown(window, PlayDimension.Client));
        Assert.Null(queries.Breakdown(window, PlayDimension.Device));
        Assert.Equal(4, queries.Total(window).Plays);
    }

    /// <summary>
    /// The reason breakdown carries the plays beside the rows, because the rows
    /// do not divide them.
    /// </summary>
    /// <remarks>
    /// One play can record several reasons, so the rows add up to more than the
    /// plays there were. A reader who adds them up and meets a play count beside
    /// them concludes the plugin is wrong, and the second number is what tells
    /// them otherwise: no row is larger than it, and the rows together can be.
    /// </remarks>
    [Fact]
    public void TheReasonBreakdownSaysHowManyPlaysItsRowsDoNotDivide()
    {
        Store(
            ATranscodedPlay(Alice, AFilm, March, "VideoCodecNotSupported", "AudioCodecNotSupported"),
            ATranscodedPlay(Bob, AnotherFilm, March.AddHours(1), "AudioCodecNotSupported"),
            APlay(Alice, AFilm, March.AddHours(2), "Jellyfin Web"));

        var reasons = new AggregateQueries(OpenTheStore).ReasonBreakdown(AWeekFrom(March));

        Assert.Equal(3, reasons.Plays);
        Assert.Equal(2, reasons.PlaysWithAtLeastOneReason);
        Assert.Equal(3, reasons.Reasons.Sum(row => row.Plays));
    }

    /// <summary>
    /// A dimension this build has no name for is refused before anything is
    /// counted.
    /// </summary>
    /// <remarks>
    /// The layer reads the accounts behind each member of a dimension before the
    /// fold does, so it spells that dimension out a second time and the two
    /// spellings have to agree. A value neither of them knows is refused here
    /// rather than counted into a group nobody named, which is what a fall
    /// through would do: every play would land under one key, the group would
    /// stand on every account on the server, and the rule would answer a
    /// breakdown it has never seen the shape of.
    /// </remarks>
    [Fact]
    public void ADimensionThisBuildHasNoNameForIsRefused()
    {
        Store(APlay(Alice, AFilm, March, "Jellyfin Web"));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AggregateQueries(OpenTheStore).Breakdown(AWeekFrom(March), (PlayDimension)99));
    }

    /// <summary>
    /// A range with no plays in it is answered rather than withheld.
    /// </summary>
    /// <remarks>
    /// The two are different facts and a shape that returned nothing for both
    /// would have destroyed the difference before a page could draw it. An empty
    /// range has no rows to stand on anybody, so answering it names nobody.
    /// </remarks>
    [Fact]
    public void AnEmptyRangeIsAnsweredRatherThanWithheld()
    {
        Store(APlay(Alice, AFilm, March, "Jellyfin Web"));

        var breakdown = new AggregateQueries(OpenTheStore).Breakdown(
            AWeekFrom(March.AddYears(1)),
            PlayDimension.Client);

        Assert.NotNull(breakdown);
        Assert.Empty(breakdown.Rows);
        Assert.Equal(0, breakdown.Plays);
    }

    /// <summary>
    /// The window is half open: a play starting at its first moment is in it and
    /// one starting at its last is not.
    /// </summary>
    /// <remarks>
    /// Two windows laid end to end therefore read each play once. A closed
    /// window would count the play on the boundary in both, and a monthly report
    /// would say the server watched more than it did, by whatever fell exactly
    /// on midnight.
    /// </remarks>
    [Fact]
    public void TheWindowIsHalfOpenAtBothEnds()
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web"),
            APlay(Bob, AnotherFilm, March.AddDays(1), "Jellyfin Web"));

        var totals = new AggregateQueries(OpenTheStore)
            .Total(QueryWindow.Of(March, March.AddDays(1)));

        Assert.Equal(1, totals.Plays);
    }

    /// <summary>
    /// A shape refuses a range holding more plays than the window's bound
    /// allows, rather than answering out of the rows that fitted.
    /// </summary>
    /// <remarks>
    /// This case asserted the other answer until issue #56's first condition
    /// was built: twenty plays under a bound of five came back as a total of
    /// five, and the case called that the bound working. It is the failure the
    /// issue is about. Five is what the server watched, as far as any reader of
    /// that total could tell, and nothing on the answer said a quarter of the
    /// range had been read. What the bound is worth is that the work is capped;
    /// what a caller is owed is being told when the cap was reached.
    /// </remarks>
    [Fact]
    public void AShapeRefusesARangeHoldingMorePlaysThanTheBoundAllows()
    {
        var plays = new List<PlayRecord>();
        for (var i = 0; i < 20; i++)
        {
            plays.Add(APlay(Alice, AFilm, March.AddMinutes(i), "Jellyfin Web"));
        }

        Store(plays.ToArray());

        var refused = Assert.Throws<TooManyPlaysToAnswerException>(
            () => new AggregateQueries(OpenTheStore)
                .Total(QueryWindow.Of(March, March.AddDays(1), mostPlays: 5)));

        Assert.Equal(5, refused.MostPlays);
    }

    /// <summary>
    /// A bound above the ceiling is held down to it rather than granted.
    /// </summary>
    [Fact]
    public void ABoundAboveTheCeilingIsHeldDownToIt()
    {
        Assert.Equal(
            QueryWindow.MostPlaysAnyShapeReads,
            QueryWindow.Of(March, March.AddDays(1), mostPlays: int.MaxValue).MostPlays);
    }

    /// <summary>
    /// A window refuses a bound that does not say it is in UTC.
    /// </summary>
    /// <remarks>
    /// A local moment read as UTC moves the range by the caller's offset, and
    /// the report then covers a period nobody asked for with nothing on it
    /// saying so.
    /// </remarks>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void AWindowRefusesABoundThatIsNotInUtc(DateTimeKind kind)
    {
        var loose = DateTime.SpecifyKind(March, kind);

        Assert.Throws<ArgumentException>(() => QueryWindow.Of(loose, March.AddDays(1)));
        Assert.Throws<ArgumentException>(() => QueryWindow.Of(March, DateTime.SpecifyKind(March.AddDays(1), kind)));
    }

    /// <summary>
    /// A window refuses an end before its start.
    /// </summary>
    /// <remarks>
    /// Reversed bounds read as an empty range, so a report over them would
    /// answer nothing and say it had answered, which is a caller's mistake
    /// returned as a fact about the server.
    /// </remarks>
    [Fact]
    public void AWindowRefusesAnEndBeforeItsStart()
    {
        Assert.Throws<ArgumentException>(() => QueryWindow.Of(March.AddDays(1), March));
    }

    /// <summary>
    /// The top list is ordered by watched time and not by play count.
    /// </summary>
    /// <remarks>
    /// The two disagree often enough to matter. A series left running is many
    /// plays and little watching, and a list ordered by count would put it above
    /// the film somebody actually sat through.
    /// </remarks>
    [Fact]
    public void TheTopListIsOrderedByWatchedTimeAndNotByPlayCount()
    {
        // Both accounts watch both films, because a list every row of which
        // stands on one account is withheld whole and there would be nothing to
        // order. The figures still separate the two films: the first is many
        // short plays, the second is one long one.
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web", watched: TimeSpan.FromMinutes(2)),
            APlay(Alice, AFilm, March.AddHours(1), "Jellyfin Web", watched: TimeSpan.FromMinutes(2)),
            APlay(Bob, AFilm, March.AddHours(2), "Jellyfin Web", watched: TimeSpan.FromMinutes(2)),
            APlay(Alice, AnotherFilm, March.AddHours(3), "Jellyfin Web", watched: TimeSpan.FromMinutes(45)),
            APlay(Bob, AnotherFilm, March.AddHours(5), "Jellyfin Web", watched: TimeSpan.FromMinutes(45)));

        var top = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 10);

        Assert.NotNull(top);
        Assert.Equal(AnotherFilm, top[0].Key);
        Assert.Equal(TimeSpan.FromMinutes(90), top[0].Watched);
        Assert.Equal(AFilm, top[1].Key);
        Assert.Equal(3, top[1].Plays);
    }

    /// <summary>
    /// The top list returns no more rows than it was asked for.
    /// </summary>
    [Fact]
    public void TheTopListReturnsNoMoreRowsThanItWasAskedFor()
    {
        Store(
            APlay(Alice, AFilm, March, "Jellyfin Web"),
            APlay(Bob, AFilm, March.AddHours(1), "Jellyfin Web"),
            APlay(Alice, AnotherFilm, March.AddHours(2), "Jellyfin Web"),
            APlay(Bob, AnotherFilm, March.AddHours(3), "Jellyfin Web"));

        var top = new AggregateQueries(OpenTheStore).Top(AWeekFrom(March), 1);

        Assert.NotNull(top);
        Assert.Single(top);
    }

    /// <summary>
    /// A store that cannot be opened reaches the caller as an outage rather than
    /// as an empty report.
    /// </summary>
    /// <remarks>
    /// The type the endpoints translate into a status saying the plugin is
    /// unavailable. A layer that answered a broken file with no rows would tell
    /// an operator their server had been quiet.
    /// </remarks>
    [Fact]
    public void AStoreThatCannotBeOpenedReachesTheCallerAsAnOutage()
    {
        var queries = new AggregateQueries(() => throw new IOException("The file is not a database."));

        Assert.Throws<StoreCouldNotBeOpenedException>(() => queries.Total(AWeekFrom(March)));
    }

    /// <summary>
    /// One shape's answer, written out so two readings of it can be compared
    /// whatever type it came back as.
    /// </summary>
    /// <remarks>
    /// A string rather than the object, because the five answers have five types
    /// and what the case above asserts is that the answer did not move. Every
    /// figure a shape returns is in it, so a difference anywhere shows up as a
    /// difference here.
    /// </remarks>
    /// <param name="queries">The layer.</param>
    /// <param name="shape">Which shape to ask.</param>
    /// <returns>The answer, written out.</returns>
    private static string Answer(AggregateQueries queries, string shape)
    {
        var window = AWeekFrom(March);
        var zone = TimeZoneInfo.Utc;

        return shape switch
        {
            "total" => queries.Total(window).ToString(),
            "series" => string.Join(
                "; ",
                queries.Series(window, zone).Rows.Select(row => row.ToString())),
            "distribution" => string.Join(
                "; ",
                queries.Distribution(window, zone).Cells.Select(cell => cell.ToString())),
            "breakdown" => queries.Breakdown(window, PlayDimension.Client) is { } rows
                ? string.Join("; ", rows.Rows.Select(row => row.ToString()))
                : "withheld",
            "reasons" => string.Join(
                "; ",
                queries.ReasonBreakdown(window).Reasons.Select(row => row.ToString())),
            "top" => queries.Top(window, 10) is { } top
                ? string.Join("; ", top.Select(row => row.ToString()))
                : "withheld",
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "That is not one of the shapes this layer offers.")
        };
    }

    private static QueryWindow AWeekFrom(DateTime fromUtc) => QueryWindow.Of(fromUtc, fromUtc.AddDays(7));

    private SqlitePlayStore OpenTheStore() => new(_root);

    private void Store(params PlayRecord[] plays)
    {
        using var store = new SqlitePlayStore(_root);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }

    private static PlayRecord ATranscodedPlay(
        Guid userId,
        Guid itemId,
        DateTime startedUtc,
        params string[] reasons)
    {
        var play = APlay(userId, itemId, startedUtc, "Jellyfin Web");

        return play with
        {
            PlayMethodAtStart = PlayMethod.Transcode,
            Transcode = play.Transcode with
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = false,
                Reasons = reasons
            }
        };
    }

    private static PlayRecord APlay(
        Guid userId,
        Guid itemId,
        DateTime startedUtc,
        string client,
        string deviceId = "device-1",
        TimeSpan? watched = null)
    {
        var length = watched ?? TimeSpan.FromMinutes(40);

        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Movie",
            ParentId = null,
            ItemName = itemId == AFilm ? "A Film" : "Another Film",
            ItemRuntime = TimeSpan.FromMinutes(90),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc + length,
            WatchedDuration = length,
            ReachedTheEnd = false,
            ClientName = client,
            DeviceId = deviceId,
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
}
