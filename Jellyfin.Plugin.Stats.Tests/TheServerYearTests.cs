// The server-wide wrap-up, and the one shape in this plugin that puts an account
// on a server-wide answer. Issue #68, and issue #42's second condition, which
// has no other by-user view in the plan to be asserted against.
//
// Everything is driven against a real store on disk, because two of the three
// conditions are about what a response holds and the third is about a fold
// agreeing with itself across one reading of a moving store. A case over an
// in-memory sequence would pass over a store that never wrote a consent row.
//
// Berlin is the zone throughout, because a play late at night there belongs to
// the next day in UTC, so a fold that fell back to UTC comes out a day off
// rather than passing quietly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class TheServerYearTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private static readonly Guid Bob = new("22222222222222222222222222222222");

    private static readonly Guid Cass = new("33333333333333333333333333333333");

    private readonly string _root;

    public TheServerYearTests()
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
    /// The first condition of issue #68, asserted against the response rather
    /// than against the fold. No account that has not recorded consent appears
    /// anywhere in what an administrator is handed.
    /// </summary>
    /// <remarks>
    /// The assertion is over the whole body as text and not over the leaderboard
    /// alone, and that is deliberate. An identifier that leaked into a top list
    /// key, into a client breakdown row or into a field somebody adds next year
    /// would pass a case that only walked the rows this fold means to put an
    /// account on, and the condition is about the response.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheServerYearNamesNobodyWhoHasNotAgreed()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).Concat(AYearOfPlays(Cass)).ToArray());
        Agree(Ada);

        using var endpoints = Serving();

        var answer = await endpoints.Send("GET", "/Stats/Reports/Year/2026", Caller.Administrator);

        Assert.Equal(200, answer.Status);
        Assert.Contains(Ada.ToString("N"), Squashed(answer.Body), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Bob.ToString("N"), Squashed(answer.Body), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Cass.ToString("N"), Squashed(answer.Body), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Issue #42's second condition, which has no other view in this plugin to
    /// be asserted against: withdrawing consent takes the account off the answer
    /// on the NEXT request, with nothing kept in between.
    /// </summary>
    /// <remarks>
    /// Two requests against one running set of endpoints, so a register read
    /// once and held would pass the first and fail the second.
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AWithdrawalTakesAnAccountOffTheVeryNextRequest()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).Concat(AYearOfPlays(Cass)).ToArray());
        Agree(Ada);

        using var endpoints = Serving();

        var whileAgreed = await endpoints.Send("GET", "/Stats/Reports/Year/2026", Caller.Administrator);

        Assert.Equal(200, whileAgreed.Status);
        Assert.Contains(Ada.ToString("N"), Squashed(whileAgreed.Body), StringComparison.OrdinalIgnoreCase);

        Withdraw(Ada);

        var afterWithdrawing = await endpoints.Send("GET", "/Stats/Reports/Year/2026", Caller.Administrator);

        Assert.Equal(200, afterWithdrawing.Status);
        Assert.DoesNotContain(Ada.ToString("N"), Squashed(afterWithdrawing.Body), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The second condition of issue #68. On a server where nobody has agreed,
    /// every item and client figure still comes back, because none of them is
    /// per account.
    /// </summary>
    /// <remarks>
    /// This is the shape the whole plugin is meant to degrade to on a server
    /// with no consent recorded anywhere: fewer breakdowns, the same content
    /// figures, nothing invented and nobody named. The leaderboard is still
    /// answered here because three accounts stand behind the group everybody was
    /// folded into, and it carries no identifier at all.
    /// </remarks>
    [Fact]
    public void AServerWhereNobodyHasAgreedStillAnswersEveryItemAndClientFigure()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).Concat(AYearOfPlays(Cass)).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        Assert.True(year.Figures.AnythingRecorded);
        Assert.NotNull(year.Figures.Plays);
        Assert.NotNull(year.Figures.Watched);
        Assert.NotNull(year.Figures.DistinctItems);
        Assert.NotNull(year.Figures.LongestPlay);
        Assert.NotEmpty(year.Figures.TopItems);
        Assert.NotEmpty(year.Figures.TopSeries);
        Assert.NotNull(year.Clients);
        Assert.NotEmpty(year.Clients!.Rows);
        Assert.NotNull(year.Reasons);

        Assert.NotNull(year.Leaderboard);
        Assert.All(year.Leaderboard!.Rows, row => Assert.Null(row.UserId));
        Assert.Equal(3, year.Leaderboard.AccountsFolded);
    }

    /// <summary>
    /// The third condition of issue #68. The server's totals agree with the sum
    /// over the per-account wrap-ups, on rows nobody chose.
    /// </summary>
    /// <remarks>
    /// Both sides are computed here rather than read off a response, which is
    /// what the note of 2026-08-20 asks for: what the RESPONSE renders must NOT
    /// reconcile against the total, because a breakdown that adds up exactly to
    /// a published figure is a breakdown a reader can subtract from. The
    /// agreement being asserted is between the server's own fold and the sum
    /// over the accounts, and the leaderboard is deliberately not part of it.
    /// <para>
    /// The server figure is a fold over every row and not the sum, so this is an
    /// agreement between two computations rather than a definition restated. A
    /// fold defined as the sum would pass this case having proved the addition.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheServerTotalsAgreeWithTheSumOverThePerAccountYears()
    {
        var everybody = new[] { Ada, Bob, Cass };
        var generator = new Random(20260830);
        var plays = new List<PlayRecord>();

        for (var i = 0; i < 300; i++)
        {
            plays.Add(APlay(
                everybody[generator.Next(0, everybody.Length)],
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(generator.Next(0, 500_000)),
                TimeSpan.FromSeconds(generator.Next(0, 7200)),
                reachedTheEnd: generator.Next(0, 2) == 0,
                clientName: generator.Next(0, 2) == 0 ? "Jellyfin Web" : "Jellyfin Android",
                itemId: new Guid($"4444444444444444444444444444444{generator.Next(0, 6)}")));
        }

        Seed(plays.ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var server = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        var accounts = everybody
            .Select(who => AggregateQueries.AYearOver(store, who, 2026, Berlin, topCount: 5))
            .ToList();

        Assert.Equal(accounts.Sum(year => year.Plays ?? 0), server.Figures.Plays);
        Assert.Equal(
            accounts.Aggregate(TimeSpan.Zero, (total, year) => total + (year.Watched ?? TimeSpan.Zero)),
            server.Figures.Watched);
        Assert.Equal(accounts.Sum(year => year.Finished ?? 0), server.Figures.Finished);
        Assert.Equal(accounts.Sum(year => year.Abandoned ?? 0), server.Figures.Abandoned);
        Assert.Equal(accounts.Sum(year => year.Delivery?.Transcode ?? 0), server.Figures.Delivery!.Transcode);
        Assert.Equal(server.Figures.Plays, server.Figures.Finished + server.Figures.Abandoned);

        // The distinct items are NOT the sum: two accounts that watched the same
        // item are one distinct item on the server and two across the accounts.
        // It is asserted as a bound rather than left out, because a fold that
        // added them up would pass every line above.
        Assert.True(server.Figures.DistinctItems <= accounts.Sum(year => year.DistinctItems ?? 0));
        Assert.True(server.Figures.DistinctItems > 0);
    }

    /// <summary>
    /// A leaderboard whose combined group stands on one account is not answered
    /// at all, and the figures beside it are.
    /// </summary>
    /// <remarks>
    /// The subtraction is the whole reason. With Ada and Bob named and Cass
    /// alone in the group, the rest is Cass under another name, and giving that
    /// row no identifier does not change who it is about. This is issue #41's
    /// rule applied to the one dimension that is an account, and the withhold is
    /// of the whole leaderboard rather than of the group, because a list whose
    /// absences are readable is a list a reader can subtract from.
    /// </remarks>
    [Fact]
    public void ALeaderboardWhoseRestStandsOnOneAccountIsNotAnsweredAtAll()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).Concat(AYearOfPlays(Cass)).ToArray());
        Agree(Ada);
        Agree(Bob);

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        Assert.Null(year.Leaderboard);
        Assert.True(year.Figures.AnythingRecorded);
        Assert.NotNull(year.Figures.Plays);
    }

    /// <summary>
    /// Where everybody with a play has agreed, every row is named and there is
    /// no combined group, so nothing is withheld.
    /// </summary>
    /// <remarks>
    /// The near miss for the case above. A withhold written as fewer than two
    /// named rows, or as any account not named, would pass that one and fail
    /// this, and what the rule is actually about is the size of the remainder.
    /// </remarks>
    [Fact]
    public void AServerWhereEverybodyAgreedIsNamedInFullWithNoGroup()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).ToArray());
        Agree(Ada);
        Agree(Bob);

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        Assert.NotNull(year.Leaderboard);
        Assert.Equal(0, year.Leaderboard!.AccountsFolded);
        Assert.Equal(2, year.Leaderboard.Rows.Count);
        Assert.All(year.Leaderboard.Rows, row => Assert.NotNull(row.UserId));
        Assert.Equal(
            new[] { Ada, Bob }.OrderBy(who => who).ToList(),
            year.Leaderboard.Rows.Select(row => row.UserId!.Value).OrderBy(who => who).ToList());
    }

    /// <summary>
    /// A year a read refused answers that it could not be computed and carries
    /// no breakdown, no reasons and no leaderboard, rather than answering with
    /// noughts.
    /// </summary>
    /// <remarks>
    /// A nought and an unknown are different statements, and once one has been
    /// written as the other no reader can tell them apart. This is issue #64's
    /// third condition met at the shape that would produce the nought rather
    /// than at the view that would draw it.
    /// </remarks>
    [Fact]
    public void AYearAReadRefusedSaysSoAndCarriesNoFiguresAtAll()
    {
        Seed(AYearOfPlays(Ada).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(
            new EveryWindowHoldsMoreThanAReadMay(store),
            2026,
            Berlin,
            topCount: 5);

        Assert.True(year.Figures.AnythingRecorded);
        Assert.Null(year.Figures.Plays);
        Assert.Null(year.Figures.Watched);
        Assert.Equal(YearSources.NotComputed, year.Figures.Sources.Totals);
        Assert.Equal(YearSources.NotComputed, year.Figures.Sources.Detail);
        Assert.NotNull(year.Figures.Sources.NotComputedBecause);

        Assert.Null(year.Clients);
        Assert.Null(year.Reasons);
        Assert.Null(year.Leaderboard);
    }

    /// <summary>
    /// A client only one account ever used takes the whole client breakdown with
    /// it, and the year's figures stand.
    /// </summary>
    /// <remarks>
    /// The same rule and the same constant the fourth shape applies to a range,
    /// reached here through the year rather than restated for it. A row standing
    /// on one account is that account under the name of its device, and the group
    /// the thin rows fold into is thin by the same count when there is only one
    /// of them, so what is withheld is the breakdown and not the row.
    /// </remarks>
    [Fact]
    public void AClientOnlyOneAccountUsedTakesTheWholeBreakdownWithIt()
    {
        Seed(AYearOfPlays(Ada)
            .Concat(AYearOfPlays(Bob))
            .Concat(AYearOfPlays(Cass).Select(OnItsOwnClient))
            .ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        Assert.Null(year.Clients);
        Assert.True(year.Figures.AnythingRecorded);
        Assert.NotNull(year.Figures.Plays);
        Assert.NotNull(year.Reasons);
    }

    /// <summary>
    /// Where every client stands on enough accounts, the breakdown is answered
    /// in full.
    /// </summary>
    /// <remarks>
    /// The near miss for the case above. A rule that withheld the breakdown
    /// whenever more than one client appeared, or whenever any account used one
    /// client only, would pass that case and fail this.
    /// </remarks>
    [Fact]
    public void AClientEnoughAccountsUsedIsAnsweredUnderItsOwnName()
    {
        Seed(AYearOfPlays(Ada).Concat(AYearOfPlays(Bob)).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AggregateQueries.AServerYearOver(store, 2026, Berlin, topCount: 5);

        Assert.NotNull(year.Clients);
        Assert.Contains(year.Clients!.Rows, row => row.Name == "Jellyfin Web");
        Assert.Contains(year.Clients.Rows, row => row.Name == "Jellyfin Android");
    }

    /// <summary>
    /// A year outside what a calendar year can be is refused before the store is
    /// opened.
    /// </summary>
    /// <remarks>
    /// Nought reaches the fold as a date nobody can build, which would come back
    /// as a fault of the server for a request that was simply wrong. Refusing it
    /// at the route is also what keeps a caller from deciding how the store is
    /// read: the store is not opened at all.
    /// </remarks>
    /// <param name="year">The year on the request.</param>
    /// <returns>The running test.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public async Task AYearOutsideTheCalendarIsRefusedAndNothingIsOpened(int year)
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new InvalidOperationException("Nothing should be opened.")));

        var answer = await endpoints.Send("GET", $"/Stats/Reports/Year/{year}", Caller.Administrator);

        Assert.Equal(400, answer.Status);
    }

    /// <summary>
    /// A store that will not open is the plugin being unavailable rather than a
    /// year nobody watched anything in.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStoreThatWillNotOpenIsAnOutageAndNotAnEmptyYear()
    {
        using var endpoints = new InProcessEndpoints(
            reports: new AggregateQueries(() => throw new StoreCouldNotBeOpenedException()));

        var answer = await endpoints.Send("GET", "/Stats/Reports/Year/2026", Caller.Administrator);

        Assert.Equal(503, answer.Status);
    }

    private static PlayRecord OnItsOwnClient(PlayRecord play) => play with { ClientName = "A television" };

    private static string Squashed(string body) => body.Replace("-", string.Empty, StringComparison.Ordinal);

    private static IEnumerable<PlayRecord> AYearOfPlays(Guid who)
    {
        for (var month = 1; month <= 12; month++)
        {
            for (var play = 0; play < 3; play++)
            {
                yield return APlay(
                    who,
                    new DateTime(2026, month, 5 + play, 20, 0, 0, DateTimeKind.Utc),
                    TimeSpan.FromMinutes(30 + (play * 10)),
                    reachedTheEnd: play != 2,
                    clientName: play == 1 ? "Jellyfin Android" : "Jellyfin Web",
                    itemId: new Guid($"4444444444444444444444444444444{play}"));
            }
        }
    }

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan watched,
        bool reachedTheEnd,
        string clientName = "Jellyfin Web",
        Guid? itemId = null)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId ?? new Guid("55555555555555555555555555555555"),
            ItemType = "Episode",
            ParentId = new Guid("66666666666666666666666666666666"),
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = reachedTheEnd,
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
                Reasons = Array.Empty<string>(),
            },
        };

    private InProcessEndpoints Serving()
        => new(reports: new AggregateQueries(() => new SqlitePlayStore(_root, Berlin)));

    private void Agree(Guid who) => Record(who, agreed: true, withdrawnUtc: null);

    private void Withdraw(Guid who)
        => Record(who, agreed: false, withdrawnUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

    private void Record(Guid who, bool agreed, DateTime? withdrawnUtc)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Berlin);

        store.RecordConsent(new ConsentRecord
        {
            UserId = who,
            Agreed = agreed,
            AgreedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            WithdrawnUtc = withdrawnUtc,
            WordingVersion = 1,
        });
    }

    private void Seed(params PlayRecord[] plays)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Berlin);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }

    /// <summary>
    /// A store whose every window holds more rows than a read may hold, over one
    /// that holds real ones.
    /// </summary>
    /// <remarks>
    /// The refusal is produced by the bound rather than by a store that answers
    /// nothing, so the case reaches the same branch a real server over the cap
    /// would. Every other read goes to the store behind it, so the oldest row
    /// and the consent register are the real ones and the answer is not empty
    /// for a second reason.
    /// </remarks>
    private sealed class EveryWindowHoldsMoreThanAReadMay : IPlayStore
    {
        private readonly IPlayStore _behind;

        public EveryWindowHoldsMoreThanAReadMay(IPlayStore behind) => _behind = behind;

        public TimeZoneInfo? RollupZone => _behind.RollupZone;

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
            => Enumerable
                .Repeat(APlay(Ada, fromUtc, TimeSpan.FromMinutes(1), reachedTheEnd: true), limit)
                .ToList();

        public void Add(PlayRecord play) => _behind.Add(play);

        public void NoteOpenPlay(OpenPlay play) => _behind.NoteOpenPlay(play);

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
            => _behind.AddAndForgetOpenPlay(play, playKey);

        public void ForgetOpenPlay(string playKey) => _behind.ForgetOpenPlay(playKey);

        public IEnumerable<OpenPlay> OpenPlays() => _behind.OpenPlays();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => _behind.MostRecentPlays(limit);

        public IEnumerable<PlayRecord> AllPlays() => _behind.AllPlays();

        public IEnumerable<DailyRollup> AllRollups() => _behind.AllRollups();

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit)
            => _behind.RollupsFor(userId, fromDay, toDay, limit);

        public void RebuildRollups() => _behind.RebuildRollups();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => _behind.PlaysFor(userId);

        public IReadOnlyList<Guid> UserIdsWithPlays() => _behind.UserIdsWithPlays();

        public IReadOnlyList<Guid> UserIdsWithConsent() => _behind.UserIdsWithConsent();

        public DateTime? OldestPlayStartedUtc() => _behind.OldestPlayStartedUtc();

        public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone)
            => _behind.YearsWithPlaysFor(userId, zone);

        public long CountPlaysStartedBefore(DateTime cutoffUtc) => _behind.CountPlaysStartedBefore(cutoffUtc);

        public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysStartedBefore(cutoffUtc, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, deletionClass, limit);

        public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit)
            => _behind.DeletePlaysFor(userId, fromUtc, toUtc, deletionClass, limit);

        public long CountRollupsBefore(DateOnly day) => _behind.CountRollupsBefore(day);

        public int DeleteRollupsBefore(DateOnly day, DeletionClass deletionClass, int limit) => _behind.DeleteRollupsBefore(day, deletionClass, limit);

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => _behind.DeletionsRecorded(limit);

        public ConsentRecord? ConsentFor(Guid userId) => _behind.ConsentFor(userId);

        public void RecordConsent(ConsentRecord consent) => _behind.RecordConsent(consent);

        public void ForgetConsentFor(Guid userId) => _behind.ForgetConsentFor(userId);

        public void ReclaimFreedSpace() => _behind.ReclaimFreedSpace();

        public void Dispose() => _behind.Dispose();
    }
}
