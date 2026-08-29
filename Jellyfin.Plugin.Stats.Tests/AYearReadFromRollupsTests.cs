// A year folded from the day-by-day rollups and from bounded windows of play
// rows. Issues #254 and #66.
//
// Everything here is driven against a real store on disk, because what these
// conditions are about is which reads a report issues and what survives a
// deletion. A case over an in-memory sequence would pass over a store that never
// wrote a rollup and over a report that walked every row it has, which are the
// two failures this exists against.
//
// The zone is chosen by each case and nothing reads a clock or a machine
// setting. Berlin is used where a boundary matters, because a play at eleven at
// night there belongs to the next day in UTC, so a case that fell back to UTC
// comes out a day off rather than passing quietly.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class AYearReadFromRollupsTests : IDisposable
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private static readonly Guid Bob = new("22222222222222222222222222222222");

    private readonly string _root;

    public AYearReadFromRollupsTests()
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
    /// The first condition of issue #254, from the side a test can hold. The
    /// report reads the rollups and the play rows a month at a time, and it
    /// never asks for one account's whole history.
    /// </summary>
    /// <remarks>
    /// The store is watched rather than the answer, because the answer is the
    /// same either way and what this issue is about is which reads produced it.
    /// The unbounded walk this replaces is named by the one read that must not
    /// happen, and the twelve windows are counted rather than being asserted as
    /// "more than one", because eleven would mean a month of the year is missing
    /// from every figure the rows feed.
    /// </remarks>
    [Fact]
    public void AYearIsReadAsRollupsAndTwelveWindowsAndNeverAsOneAccountsWholeHistory()
    {
        Seed(APlay(Ada, June(10), TimeSpan.FromMinutes(30), reachedTheEnd: true));

        using var store = new SqlitePlayStore(_root, Berlin);
        var watched = new AStoreThatSaysWhatWasAskedOfIt(store);

        AYearFromTheStore.For(watched, Ada, 2026, Berlin, topCount: 5);

        Assert.Equal(1, watched.RollupRangeReads);
        Assert.Equal(12, watched.PlayWindowReads);
        Assert.Equal(0, watched.WholeHistoryWalks);
    }

    /// <summary>
    /// The arithmetic, taken the other way. Every figure the rollups feed agrees
    /// with the same figure folded straight off the play rows.
    /// </summary>
    /// <remarks>
    /// This is what makes the case above worth anything: a report that read the
    /// rollups and got them wrong would satisfy the reads and tell a person a
    /// year that never happened. The rows are spread over several months, two
    /// clients, two kinds of item and four delivery methods, so a fold that
    /// added the wrong column or added one day's rows twice comes out different
    /// rather than the same.
    /// </remarks>
    [Fact]
    public void EveryFigureOffTheRollupsAgreesWithTheSameFigureOffTheRows()
    {
        Seed(AYearOfPlays().ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var fromRollups = AYearFromTheStore.For(store, Ada, 2026, Berlin, topCount: 5);
        var fromRows = YearInReview.Over(
            store.PlaysFor(Ada),
            Ada,
            2026,
            Berlin,
            5,
            store.OldestPlayStartedUtc());

        Assert.Equal(YearSources.Aggregates, fromRollups.Sources.Totals);
        Assert.Equal(YearSources.Plays, fromRollups.Sources.Detail);
        Assert.Null(fromRollups.Sources.NotComputedBecause);

        Assert.Equal(fromRows.Plays, fromRollups.Plays);
        Assert.Equal(fromRows.Watched, fromRollups.Watched);
        Assert.Equal(fromRows.Finished, fromRollups.Finished);
        Assert.Equal(fromRows.Abandoned, fromRollups.Abandoned);
        Assert.Equal(fromRows.BusiestDay, fromRollups.BusiestDay);
        Assert.Equal(fromRows.BusiestMonth, fromRollups.BusiestMonth);
        Assert.Equal(fromRows.Delivery, fromRollups.Delivery);

        // The four the rollups cannot carry, which come off the windows and must
        // be the same as the ones a single walk produced.
        Assert.Equal(fromRows.DistinctItems, fromRollups.DistinctItems);
        Assert.Equal(fromRows.LongestPlay, fromRollups.LongestPlay);
        Assert.Equal(fromRows.TopItems, fromRollups.TopItems);
        Assert.Equal(fromRows.TopSeries, fromRollups.TopSeries);
    }

    /// <summary>
    /// The third condition of issue #254. A store holding rollups and no rows
    /// for a year still answers that year, and the answer says which of its
    /// figures the rows are no longer behind.
    /// </summary>
    /// <remarks>
    /// The state is produced rather than described: a retention deletion leaves
    /// the rollups it aged out of standing, which is what issue #253 settled, so
    /// this deletes the year's rows through the store and reads the year
    /// afterwards.
    /// </remarks>
    [Fact]
    public void AStoreHoldingRollupsAndNoRowsStillAnswersTheYearAndSaysWhatIsMissing()
    {
        Seed(AYearOfPlays().ToArray());

        using (var sweeping = new SqlitePlayStore(_root, Berlin))
        {
            sweeping.DeletePlaysStartedBefore(
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DeletionClass.Retention,
                limit: 10_000);
        }

        using var store = new SqlitePlayStore(_root, Berlin);

        Assert.Empty(store.PlaysFor(Ada));

        var year = AYearFromTheStore.For(store, Ada, 2026, Berlin, topCount: 5);

        Assert.True(year.AnythingRecorded);
        Assert.Equal(YearSources.Aggregates, year.Sources.Totals);
        Assert.Equal(YearSources.NotComputed, year.Sources.Detail);
        Assert.Contains("no longer in the store", year.Sources.NotComputedBecause);

        // The totals stand, because the aggregates outlived the rows on purpose.
        Assert.NotNull(year.Plays);
        Assert.NotNull(year.Watched);
        Assert.NotNull(year.BusiestDay);

        // And the four a row carries are absent rather than nought, because a
        // person who watched forty items and is shown none is being told
        // something false.
        Assert.Null(year.DistinctItems);
        Assert.Null(year.LongestPlay);
        Assert.Empty(year.TopItems);
        Assert.Empty(year.TopSeries);
    }

    /// <summary>
    /// The degradation rule of issue #66. A month over the bound costs exactly
    /// the figures that month would have fed and costs nothing else, and the
    /// reason arrives beside them.
    /// </summary>
    /// <remarks>
    /// The bound is driven by refusing a window rather than by writing a quarter
    /// of a million rows, because what is being asserted is what the fold does
    /// with a refusal and not what SQLite does with a large table. A year
    /// refused outright would be the failure this rule exists against: a year is
    /// a range the caller cannot shorten, so a refusal is permanent.
    /// </remarks>
    [Fact]
    public void AMonthOverTheBoundCostsItsOwnFiguresAndLeavesTheRestStanding()
    {
        Seed(AYearOfPlays().ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = YearInReview.Over(
            AYearFromTheStore.RollupsForTheYear(store, Ada, 2026, Berlin),
            (_, _) => WindowOfPlays.TooManyToRead("This month holds more plays than one read may hold."),
            Ada,
            2026,
            Berlin,
            5,
            store.OldestPlayStartedUtc());

        var whole = AYearFromTheStore.For(store, Ada, 2026, Berlin, topCount: 5);

        Assert.Equal(YearSources.Aggregates, year.Sources.Totals);
        Assert.Equal(YearSources.NotComputed, year.Sources.Detail);
        Assert.Equal("This month holds more plays than one read may hold.", year.Sources.NotComputedBecause);

        Assert.Equal(whole.Plays, year.Plays);
        Assert.Equal(whole.Watched, year.Watched);
        Assert.Equal(whole.BusiestDay, year.BusiestDay);
        Assert.Equal(whole.BusiestMonth, year.BusiestMonth);
        Assert.Equal(whole.Delivery, year.Delivery);

        Assert.Null(year.DistinctItems);
        Assert.Null(year.LongestPlay);
        Assert.Empty(year.TopItems);
    }

    /// <summary>
    /// The same refusal on a store with no rollups to fall back on. Nothing is
    /// invented: the answer says something was recorded and that none of it
    /// could be computed, which is a third statement beside a quiet year and a
    /// full one.
    /// </summary>
    [Fact]
    public void AYearWithNothingToFallBackOnSaysItCouldNotBeComputedRatherThanAnsweringWithNoughts()
    {
        var year = YearInReview.Over(
            rollups: null,
            (_, _) => WindowOfPlays.TooManyToRead("This month holds more plays than one read may hold."),
            Ada,
            2026,
            Berlin,
            5,
            oldestPlayStartedUtc: null);

        Assert.True(year.AnythingRecorded);
        Assert.Equal(YearSources.NotComputed, year.Sources.Totals);
        Assert.Equal(YearSources.NotComputed, year.Sources.Detail);
        Assert.Equal("This month holds more plays than one read may hold.", year.Sources.NotComputedBecause);

        Assert.Null(year.Plays);
        Assert.Null(year.Watched);
        Assert.Null(year.BusiestDay);
        Assert.Null(year.Delivery);
    }

    /// <summary>
    /// A store whose rollups were keyed in another zone is not read as if its
    /// days were this year's days, and the answer says the totals came off the
    /// rows instead.
    /// </summary>
    /// <remarks>
    /// A store states the zone it was first keyed in and not the one the setting
    /// names today, so this is the ordinary state of a server whose operator has
    /// changed the setting. Reading the rollups anyway would report a busiest day
    /// that is somebody else's midnight, which is a wrong figure rather than a
    /// missing one.
    /// </remarks>
    [Fact]
    public void RollupsKeyedInAnotherZoneAreNotReadAsThisYearsDays()
    {
        Seed(AYearOfPlays().ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        Assert.Null(AYearFromTheStore.RollupsForTheYear(store, Ada, 2026, Auckland));

        var year = AYearFromTheStore.For(store, Ada, 2026, Auckland, topCount: 5);

        Assert.Equal(YearSources.Plays, year.Sources.Totals);
        Assert.Equal(YearSources.Plays, year.Sources.Detail);
        Assert.Equal("Pacific/Auckland", year.ZoneId);
    }

    /// <summary>
    /// One account's year is folded from that account's rollups and nobody
    /// else's, and the fold does the filtering itself rather than trusting
    /// whoever read them.
    /// </summary>
    [Fact]
    public void OneAccountsYearIsFoldedFromTheirOwnRollupsAndNobodyElses()
    {
        Seed(AYearOfPlays().Concat(AYearOfPlays(Bob)).ToArray());

        using var store = new SqlitePlayStore(_root, Berlin);

        var hers = AYearFromTheStore.For(store, Ada, 2026, Berlin, topCount: 5);

        // Handed everybody's rollups by hand, the fold still answers about one
        // person, so a read that stopped filtering would be caught here rather
        // than by the store happening to filter for it.
        var handedEverything = YearInReview.Over(
            store.AllRollups().ToList(),
            (from, to) => AYearFromTheStore.APlayWindow(store, from, to),
            Ada,
            2026,
            Berlin,
            5,
            store.OldestPlayStartedUtc());

        Assert.Equal(hers.Plays, handedEverything.Plays);
        Assert.Equal(hers.Watched, handedEverything.Watched);
        Assert.Equal(hers.Delivery, handedEverything.Delivery);
    }

    /// <summary>
    /// A year nothing was recorded in still says so rather than saying it could
    /// not be computed. The two are different facts and this route added the
    /// second one.
    /// </summary>
    [Fact]
    public void AYearWithNothingInItStillSaysSoRatherThanSayingItCouldNotBeComputed()
    {
        Seed(APlay(Ada, June(10), TimeSpan.FromMinutes(30), reachedTheEnd: true));

        using var store = new SqlitePlayStore(_root, Berlin);

        var year = AYearFromTheStore.For(store, Ada, 2024, Berlin, topCount: 5);

        Assert.False(year.AnythingRecorded);
        Assert.Equal(YearSources.Plays, year.Sources.Totals);
        Assert.Null(year.Sources.NotComputedBecause);
    }

    /// <summary>
    /// A store that has never keyed a rollup answers with none rather than with
    /// the ones a default zone would have produced.
    /// </summary>
    /// <remarks>
    /// Null on the store means it has never keyed a rollup, and that is not the
    /// same as the zone being the default one. A reader treating the two as one
    /// would report a day boundary the store has never used.
    /// </remarks>
    [Fact]
    public void AStoreThatHasNeverKeyedARollupOffersNone()
    {
        Directory.CreateDirectory(_root);

        using var behind = new SqlitePlayStore(_root, Berlin);

        // Opening this store keys a zone, so the state is reached through a
        // store that says it has keyed none rather than by finding one that
        // has. The interface admits the answer and a report has to decide what
        // to do with it, which is what this holds.
        var store = new AStoreThatSaysWhatWasAskedOfIt(behind, keyedNothing: true);

        Assert.Null(store.RollupZone);
        Assert.Null(AYearFromTheStore.RollupsForTheYear(store, Ada, 2026, Berlin));
        Assert.Equal(0, store.RollupRangeReads);
    }

    /// <summary>
    /// A year holding more rollup rows than the read may bring back is answered
    /// from the play rows instead, and never from the rows that fitted.
    /// </summary>
    /// <remarks>
    /// A truncated year would be a wrap-up that is wrong by whatever it did not
    /// read with nothing on it saying so, which is the failure issue #56 is
    /// about met from the aggregate side. The store here answers with one row
    /// more than the bound allows, which is the shape the read asks for so that
    /// reaching the bound can be told from filling it exactly.
    /// </remarks>
    [Fact]
    public void AYearOverTheRollupBoundIsAnsweredFromTheRowsAndNeverFromWhatFitted()
    {
        Directory.CreateDirectory(_root);

        using var behind = new SqlitePlayStore(_root, Berlin);
        var store = new AStoreAnsweringWithMoreThanTheBound(behind);

        Assert.Null(AYearFromTheStore.RollupsForTheYear(store, Ada, 2026, Berlin));
    }

    /// <summary>
    /// A month holding more plays than one read may hold comes back as a
    /// refusal carrying the bound, rather than as the rows that fitted.
    /// </summary>
    /// <remarks>
    /// The store answers with one row more than the read asked to hold, which is
    /// how the read is written and is the only way reaching the bound can be
    /// told from filling it exactly. The rows are counted rather than built, so
    /// the case says what a quarter of a million rows would do without writing
    /// a quarter of a million rows.
    /// </remarks>
    [Fact]
    public void AMonthOverTheBoundComesBackAsARefusalCarryingTheBound()
    {
        Directory.CreateDirectory(_root);

        using var behind = new SqlitePlayStore(_root, Berlin);
        var store = new AStoreAnsweringWithMoreThanTheBound(behind);

        var window = AYearFromTheStore.APlayWindow(store, June(1), June(30));

        Assert.Empty(window.Plays);
        Assert.Contains(
            QueryWindow.MostPlaysAnyShapeReads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            window.OverTheBound);
    }

    private static DateTime June(int day) => new(2026, 6, day, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Rows across several months, two clients, two kinds of item and all four
    /// delivery methods, so a fold that read the wrong column comes out
    /// different rather than the same.
    /// </summary>
    private static IEnumerable<PlayRecord> AYearOfPlays(Guid? who = null)
    {
        var userId = who ?? Ada;
        var methods = new[]
        {
            PlayMethod.DirectPlay,
            PlayMethod.DirectStream,
            PlayMethod.Transcode,
            PlayMethod.Unknown,
        };

        for (var month = 1; month <= 12; month++)
        {
            for (var play = 0; play < 3; play++)
            {
                yield return APlay(
                    userId,
                    new DateTime(2026, month, 1 + play, 20, 0, 0, DateTimeKind.Utc),
                    TimeSpan.FromMinutes(10 + (month * 3) + play),
                    reachedTheEnd: play != 2,
                    itemType: play == 0 ? "Movie" : "Episode",
                    clientName: play == 1 ? "Jellyfin Android" : "Jellyfin Web",
                    method: methods[(month + play) % methods.Length],
                    itemId: new Guid($"4444444444444444444444444444444{play}"));
            }
        }
    }

    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan watched,
        bool reachedTheEnd,
        string itemType = "Episode",
        string clientName = "Jellyfin Web",
        PlayMethod method = PlayMethod.DirectPlay,
        Guid? itemId = null)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId ?? new Guid("33333333333333333333333333333333"),
            ItemType = itemType,
            ParentId = new Guid("55555555555555555555555555555555"),
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
            PlayMethodAtStart = method,
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
    /// A store whose two bounded reads answer with one row more than the read
    /// asked to hold.
    /// </summary>
    /// <remarks>
    /// The rows are counted rather than built. Both reads decide on the count
    /// before they look at a row, so a list that knows its length and makes a
    /// row only when one is asked for says what a quarter of a million rows
    /// would say without writing them, and the case stays a case about the
    /// bound rather than about how fast SQLite writes.
    /// </remarks>
    private sealed class AStoreAnsweringWithMoreThanTheBound : IPlayStore
    {
        private readonly IPlayStore _behind;

        public AStoreAnsweringWithMoreThanTheBound(IPlayStore behind)
        {
            _behind = behind;
        }

        public TimeZoneInfo? RollupZone => Berlin;

        public void Add(PlayRecord play) => _behind.Add(play);

        public void NoteOpenPlay(OpenPlay play) => _behind.NoteOpenPlay(play);

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
            => _behind.AddAndForgetOpenPlay(play, playKey);

        public void ForgetOpenPlay(string playKey) => _behind.ForgetOpenPlay(playKey);

        public IEnumerable<OpenPlay> OpenPlays() => _behind.OpenPlays();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => _behind.MostRecentPlays(limit);

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
            => new AsManyRowsAsAsked(limit, index => APlay(Ada, fromUtc.AddMinutes(index), TimeSpan.FromMinutes(1), reachedTheEnd: true));

        public IEnumerable<PlayRecord> AllPlays() => _behind.AllPlays();

        public IEnumerable<DailyRollup> AllRollups() => _behind.AllRollups();

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit)
            => new AsManyRollupsAsAsked(limit, userId, fromDay);

        public void RebuildRollups() => _behind.RebuildRollups();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId) => _behind.PlaysFor(userId);

        public IReadOnlyList<Guid> UserIdsWithPlays() => _behind.UserIdsWithPlays();

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

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => _behind.DeletionsRecorded(limit);

        public ConsentRecord? ConsentFor(Guid userId) => _behind.ConsentFor(userId);

        public void RecordConsent(ConsentRecord consent) => _behind.RecordConsent(consent);

        public void ForgetConsentFor(Guid userId) => _behind.ForgetConsentFor(userId);

        public void ReclaimFreedSpace() => _behind.ReclaimFreedSpace();

        public void Dispose()
        {
            // The store behind this one is owned by the case and closed there.
        }
    }

    /// <summary>
    /// As many play rows as a read asked to hold, made one at a time.
    /// </summary>
    private sealed class AsManyRowsAsAsked : IReadOnlyList<PlayRecord>
    {
        private readonly Func<int, PlayRecord> _row;

        public AsManyRowsAsAsked(int count, Func<int, PlayRecord> row)
        {
            Count = count;
            _row = row;
        }

        public int Count { get; }

        public PlayRecord this[int index] => _row(index);

        public IEnumerator<PlayRecord> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return _row(index);
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// As many rollup rows as a read asked to hold, made one at a time.
    /// </summary>
    private sealed class AsManyRollupsAsAsked : IReadOnlyList<DailyRollup>
    {
        private readonly Guid _userId;

        private readonly DateOnly _from;

        public AsManyRollupsAsAsked(int count, Guid userId, DateOnly from)
        {
            Count = count;
            _userId = userId;
            _from = from;
        }

        public int Count { get; }

        public DailyRollup this[int index] => new()
        {
            Day = _from.AddDays(index % 365),
            UserId = _userId,
            ItemType = "Episode",
            ClientName = "Jellyfin Web",
            Plays = 1,
            Watched = TimeSpan.FromMinutes(1),
            Completed = 1,
            UnknownMethod = 0,
            DirectPlay = 1,
            DirectStream = 0,
            Transcode = 0,
        };

        public IEnumerator<DailyRollup> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A store that answers exactly as the one behind it and counts which reads
    /// were asked of it.
    /// </summary>
    /// <remarks>
    /// Only the three reads a year is about are counted. Everything else is
    /// passed straight through, so this cannot change what the fold sees and can
    /// only say what it asked for.
    /// </remarks>
    private sealed class AStoreThatSaysWhatWasAskedOfIt : IPlayStore
    {
        private readonly IPlayStore _behind;

        private readonly bool _keyedNothing;

        public AStoreThatSaysWhatWasAskedOfIt(IPlayStore behind, bool keyedNothing = false)
        {
            _behind = behind;
            _keyedNothing = keyedNothing;
        }

        public int RollupRangeReads { get; private set; }

        public int PlayWindowReads { get; private set; }

        public int WholeHistoryWalks { get; private set; }

        public TimeZoneInfo? RollupZone => _keyedNothing ? null : _behind.RollupZone;

        public void Add(PlayRecord play) => _behind.Add(play);

        public void NoteOpenPlay(OpenPlay play) => _behind.NoteOpenPlay(play);

        public void AddAndForgetOpenPlay(PlayRecord play, string playKey)
            => _behind.AddAndForgetOpenPlay(play, playKey);

        public void ForgetOpenPlay(string playKey) => _behind.ForgetOpenPlay(playKey);

        public IEnumerable<OpenPlay> OpenPlays() => _behind.OpenPlays();

        public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => _behind.MostRecentPlays(limit);

        public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
        {
            PlayWindowReads++;

            return _behind.PlaysBetween(fromUtc, toUtc, limit);
        }

        public IEnumerable<PlayRecord> AllPlays() => _behind.AllPlays();

        public IEnumerable<DailyRollup> AllRollups() => _behind.AllRollups();

        public IReadOnlyList<DailyRollup> RollupsFor(Guid userId, DateOnly fromDay, DateOnly toDay, int limit)
        {
            RollupRangeReads++;

            return _behind.RollupsFor(userId, fromDay, toDay, limit);
        }

        public void RebuildRollups() => _behind.RebuildRollups();

        public IEnumerable<PlayRecord> PlaysFor(Guid userId)
        {
            WholeHistoryWalks++;

            return _behind.PlaysFor(userId);
        }

        public IReadOnlyList<Guid> UserIdsWithPlays() => _behind.UserIdsWithPlays();

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

        public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => _behind.DeletionsRecorded(limit);

        public ConsentRecord? ConsentFor(Guid userId) => _behind.ConsentFor(userId);

        public void RecordConsent(ConsentRecord consent) => _behind.RecordConsent(consent);

        public void ForgetConsentFor(Guid userId) => _behind.ForgetConsentFor(userId);

        public void ReclaimFreedSpace() => _behind.ReclaimFreedSpace();

        public void Dispose()
        {
            // The store behind this one is owned by the case and closed there.
        }
    }
}
