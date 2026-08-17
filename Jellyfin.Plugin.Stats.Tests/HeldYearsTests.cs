// What keeps a folded year and what makes it let go, driven over a temporary
// directory where a real deletion is what does the letting go.
//
// Two kinds of case are in here on purpose. The ones about holding count folds
// through a function the test supplies, because "how many times was this
// computed" is the question issue #70 asks and it is not answerable from a
// store. The ones about invalidation drive the deletion routes this plugin
// actually has, over a real store on disk, because a hold that only lets go
// when a test calls Forget is a method with a test rather than a plugin that
// does not serve a year whose rows have gone.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Events;
using Jellyfin.Plugin.Stats.ScheduledTasks;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class HeldYearsTests : IDisposable
{
    /// <summary>
    /// The account every case is about.
    /// </summary>
    private static readonly Guid Ada = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    /// <summary>
    /// The account that is not, and whose held year every deletion case checks
    /// is either kept or let go of deliberately. A hold that threw everything
    /// away on any change would pass every assertion about Ada alone.
    /// </summary>
    private static readonly Guid Bo = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>
    /// The moment every case runs at, so which year is finished and which is
    /// the one the server is in are values the test chose.
    /// </summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A year that has ended at <see cref="Now"/>, and the year every play
    /// written below falls in.
    /// </summary>
    private const int Finished = 2025;

    /// <summary>
    /// How many rows a top list may hold in these cases. Any positive number
    /// would do; it is named so that the case about a changed bound is
    /// obviously changing one thing.
    /// </summary>
    private const int Top = 5;

    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private readonly string _root;

    public HeldYearsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The first condition of issue #70. A finished year is folded on the first
    /// call and answered from what is kept on every one after it, and the
    /// counter is what says so: two calls returning equal answers would pass
    /// against a hold that never held anything.
    /// </summary>
    [Fact]
    public void AFinishedYearIsFoldedOnceHoweverOftenItIsOpened()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        var first = years.For(Ada, Finished, Utc, Top);
        var second = years.For(Ada, Finished, Utc, Top);
        var third = years.For(Ada, Finished, Utc, Top);

        Assert.Equal(1, folds.Calls);
        Assert.Same(first, second);
        Assert.Same(first, third);
    }

    /// <summary>
    /// The year the server is in is folded every time and kept nowhere. It can
    /// gain a play at any moment, so an answer held for it is wrong within the
    /// hour on any server anybody is watching.
    /// </summary>
    [Fact]
    public void TheYearTheServerIsInIsFoldedEveryTimeAndKeptNowhere()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Now.Year, Utc, Top);
        years.For(Ada, Now.Year, Utc, Top);

        Assert.Equal(2, folds.Calls);
        Assert.Equal(0, years.Count);
    }

    /// <summary>
    /// A year that has not started yet is not a finished one either. The test
    /// is worth having beside the case above because the two share a
    /// comparison, and a hold written with the wrong one of the two operators
    /// would keep next year's empty answer forever.
    /// </summary>
    [Fact]
    public void AYearThatHasNotHappenedIsNotKeptEither()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Now.Year + 1, Utc, Top);
        years.For(Ada, Now.Year + 1, Utc, Top);

        Assert.Equal(2, folds.Calls);
        Assert.Equal(0, years.Count);
    }

    /// <summary>
    /// Which year is finished is read in the zone the caller asked in and not
    /// in the machine's. At this moment it is the last day of a year in Sydney
    /// and still the year before in Los Angeles, so the same year is a finished
    /// one for one reader and the current one for the other.
    /// </summary>
    [Fact]
    public void WhetherAYearHasEndedIsReadInTheZoneTheCallerAsked()
    {
        var moment = new DateTimeOffset(2026, 1, 1, 6, 0, 0, TimeSpan.Zero);
        var folds = new Counter();
        var years = ClockedAt(moment, folds);

        var sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
        var losAngeles = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

        // In Sydney it is already 2026, so 2025 has ended and is kept.
        years.For(Ada, 2025, sydney, Top);
        years.For(Ada, 2025, sydney, Top);

        // In Los Angeles it is still 2025, so 2025 is the year the reader is in
        // and is folded again.
        years.For(Ada, 2025, losAngeles, Top);
        years.For(Ada, 2025, losAngeles, Top);

        Assert.Equal(3, folds.Calls);
        Assert.Equal(1, years.Count);
    }

    /// <summary>
    /// The third condition of issue #70. Nothing is folded or kept for an
    /// account that has not asked, and one account asking does not warm
    /// anything for another. There is no walk over the accounts with rows and
    /// no start-up pass, so the assertion is about this object holding nothing
    /// rather than about a table being empty.
    /// </summary>
    [Fact]
    public void NothingIsKeptForAnAccountThatHasNotAsked()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        Assert.Equal(0, years.Count);
        Assert.Equal(0, folds.Calls);

        years.For(Ada, Finished, Utc, Top);

        Assert.Equal(1, years.Count);
        Assert.Equal(1, folds.Calls);
        Assert.Equal(new[] { Ada }, folds.Asked);
    }

    /// <summary>
    /// A year read in another zone is a different answer and is folded rather
    /// than served from the one already kept. Both ends of a year move with the
    /// zone, so a play near either boundary is inside it for one reader and
    /// outside it for the other.
    /// </summary>
    [Fact]
    public void AYearReadInAnotherZoneIsFoldedRatherThanReused()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Finished, Utc, Top);
        years.For(Ada, Finished, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), Top);

        Assert.Equal(2, folds.Calls);
        Assert.Equal(2, years.Count);
    }

    /// <summary>
    /// A wider top list is folded rather than cut down from a narrower one that
    /// is already kept. A list of three is not the first three of a list of
    /// ten's worth of work, and serving one from the other would answer a
    /// question nobody asked.
    /// </summary>
    [Fact]
    public void AWiderTopListIsFoldedRatherThanServedFromANarrowerOne()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Finished, Utc, 3);
        years.For(Ada, Finished, Utc, 10);

        Assert.Equal(2, folds.Calls);
        Assert.Equal(2, years.Count);
    }

    /// <summary>
    /// Letting go of one account's years leaves everybody else's alone. A hold
    /// that cleared itself on any deletion would pass every case here that only
    /// looks at the account being deleted, and it would turn one account being
    /// removed into a re-fold for every reader on the server.
    /// </summary>
    [Fact]
    public void LettingGoOfOneAccountLeavesAnothersHeldYearWhereItIs()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Finished, Utc, Top);
        years.For(Bo, Finished, Utc, Top);

        years.Forget(Ada);

        Assert.Equal(1, years.Count);

        years.For(Bo, Finished, Utc, Top);
        years.For(Ada, Finished, Utc, Top);

        Assert.Equal(3, folds.Calls);
    }

    /// <summary>
    /// Letting go of everything is what a sweep by cutoff needs, because a
    /// cutoff names a moment and not an account.
    /// </summary>
    [Fact]
    public void LettingGoOfEverythingLeavesNothingHeld()
    {
        var folds = new Counter();
        var years = ClockedAt(Now, folds);

        years.For(Ada, Finished, Utc, Top);
        years.For(Bo, Finished, Utc, Top);

        years.ForgetEverything();

        Assert.Equal(0, years.Count);

        years.For(Ada, Finished, Utc, Top);

        Assert.Equal(3, folds.Calls);
    }

    /// <summary>
    /// A fold that was overtaken by a deletion is answered to the caller who
    /// asked for it and kept for nobody. The fold runs outside the lock on
    /// purpose, so without this the deletion would be undone by the fold
    /// storing rows that had already gone, and the account would be left
    /// holding a year computed from a store that no longer says that.
    /// <para>
    /// The deletion is raised from inside the fold, which is the only way to be
    /// certain the two overlap. A test that raced two threads would pass on a
    /// run where they did not.
    /// </para>
    /// </summary>
    [Fact]
    public void AFoldOvertakenByADeletionIsAnsweredAndNotKept()
    {
        var folds = new Counter();
        HeldYears? years = null;

        years = new HeldYears(
            (userId, year, zone, topCount) =>
            {
                var answer = folds.Fold(userId, year, zone, topCount);
                years!.Forget(userId);
                return answer;
            },
            new FixedClock(Now));

        var answered = years.For(Ada, Finished, Utc, Top);

        Assert.NotNull(answered);
        Assert.Equal(0, years.Count);
    }

    /// <summary>
    /// The second condition of issue #70, over the route a server actually
    /// takes. The year is held, the account is deleted through the consumer the
    /// container registers, and the next reader gets a fold rather than the
    /// answer computed from rows that are now gone.
    /// <para>
    /// The fold reads the store, so the two answers say different things as
    /// well as being different objects, and that is what separates a hold that
    /// was let go of from one that was never consulted.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeletingAnAccountMeansItsYearIsFoldedAgainFromWhatIsLeft()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Bo));
        }

        var folds = new Counter();
        var years = OverTheStore(folds);

        var before = years.For(Ada, Finished, Utc, Top);

        Assert.True(before.AnythingRecorded);
        Assert.Equal(2, before.Plays);

        await new UserDeletedConsumer(OpenTheStore, UserDeletedConsumer.DefaultBite, years)
            .OnEvent(new UserDeletedEventArgs(FakeUserManager.NewUser("ada", Ada)));

        var after = years.For(Ada, Finished, Utc, Top);

        Assert.Equal(2, folds.Calls);
        Assert.False(after.AnythingRecorded);

        using var left = new SqlitePlayStore(_root);
        Assert.Empty(left.PlaysFor(Ada));
    }

    /// <summary>
    /// A retention sweep that deleted rows lets go of every held year. The
    /// cutoff names a moment rather than an account, so the account whose rows
    /// the sweep took and the account whose rows it did not are both re-folded,
    /// and neither of them is serving an answer computed over a row that has
    /// gone.
    /// </summary>
    [Fact]
    public void ASweepThatDeletedRowsLetsGoOfEveryHeldYear()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Bo));
        }

        var folds = new Counter();
        var years = OverTheStore(folds);

        years.For(Ada, Finished, Utc, Top);
        years.For(Bo, Finished, Utc, Top);

        Assert.Equal(2, years.Count);

        var swept = new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite, years)
            .Run(Now.UtcDateTime, new Progress<double>(), CancellationToken.None);

        Assert.Equal(2, swept);
        Assert.Equal(0, years.Count);
    }

    /// <summary>
    /// A sweep that deleted nothing keeps what is held. It is the ordinary run
    /// on a server inside its window, and throwing every held answer away on it
    /// would be a daily cost paid for no change at all.
    /// </summary>
    [Fact]
    public void ASweepThatDeletedNothingKeepsWhatIsHeld()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
        }

        var folds = new Counter();
        var years = OverTheStore(folds);

        years.For(Ada, Finished, Utc, Top);

        var swept = new RetentionSweep(OpenTheStore, RetentionSweep.DefaultBite, years)
            .Run(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new Progress<double>(), CancellationToken.None);

        Assert.Equal(0, swept);
        Assert.Equal(1, years.Count);

        years.For(Ada, Finished, Utc, Top);

        Assert.Equal(1, folds.Calls);
    }

    /// <summary>
    /// The sweep over accounts the server no longer has lets go of the years it
    /// took rows from, and of nobody else's. That route deletes by identifier
    /// and knows which ones it touched, so it says which held years go rather
    /// than clearing everything the way the sweep by cutoff has to.
    /// </summary>
    [Fact]
    public void TheSweepOverAccountsTheServerLostLetsGoOfTheirYearAndNoOthers()
    {
        using (var store = new SqlitePlayStore(_root))
        {
            store.Add(APlayBy(Ada));
            store.Add(APlayBy(Bo));
        }

        var folds = new Counter();
        var years = OverTheStore(folds);

        years.For(Ada, Finished, Utc, Top);
        years.For(Bo, Finished, Utc, Top);

        // The server still has Bo and has never heard of Ada.
        var swept = new UnknownUserSweep(
            OpenTheStore,
            new FakeUserManager(FakeUserManager.NewUser("bo", Bo)),
            UnknownUserSweep.DefaultBite,
            years).Run(new Progress<double>(), CancellationToken.None);

        Assert.Equal(1, swept);
        Assert.Equal(1, years.Count);

        years.For(Bo, Finished, Utc, Top);

        Assert.Equal(2, folds.Calls);
    }

    /// <summary>
    /// The container hands out one hold and the same one every time. A
    /// registration that produced a fresh one per request would give every
    /// deletion route a hold of its own, each of them letting go of an answer
    /// nobody was ever served, while the one a reader was served from went on
    /// answering out of rows that had gone. Everything else in this file would
    /// still be green.
    /// <para>
    /// What this does not prove is that each of the three routes was handed
    /// that instance rather than nothing. Proving it would mean running a route
    /// built by the container, and every route opens the store through the
    /// plugin instance, which is one static for the whole process that other
    /// classes in this suite set while this one runs. A case resting on it
    /// would fail on whichever run the two overlapped. The routes are driven
    /// with a hold handed in directly instead, in the cases above.
    /// </para>
    /// </summary>
    [Fact]
    public void TheContainerHandsOutOneHoldAndTheRoutesThatEmptyItResolve()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);
        services.AddSingleton<IUserManager>(new FakeUserManager());

        using var provider = services.BuildServiceProvider();

        var years = provider.GetRequiredService<HeldYears>();

        Assert.Same(years, provider.GetRequiredService<HeldYears>());
        Assert.NotNull(provider.GetRequiredService<RetentionSweep>());
        Assert.NotNull(provider.GetRequiredService<UnknownUserSweep>());
        Assert.Single(provider.GetServices<IEventConsumer<UserDeletedEventArgs>>());
    }

    /// <summary>
    /// The arguments that cannot be absent, refused where they are taken rather
    /// than at the first line that would have used them.
    /// </summary>
    [Fact]
    public void WhatCannotBeAbsentIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new HeldYears(null!, new FixedClock(Now)));
        Assert.Throws<ArgumentNullException>(
            () => new HeldYears((_, _, _, _) => throw new InvalidOperationException(), null!));

        var years = ClockedAt(Now, new Counter());

        Assert.Throws<ArgumentNullException>(() => years.For(Ada, Finished, null!, Top));
        Assert.Throws<ArgumentOutOfRangeException>(() => years.For(Ada, Finished, Utc, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => years.For(Ada, Finished, Utc, -1));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static HeldYears ClockedAt(DateTimeOffset now, Counter folds)
        => new(folds.Fold, new FixedClock(now));

    private static PlayRecord APlayBy(Guid userId)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Movie",
            ParentId = null,
            ItemName = "An item",
            ItemRuntime = TimeSpan.FromMinutes(90),
            StartedUtc = new DateTime(Finished, 6, 1, 9, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(Finished, 6, 1, 9, 41, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethod = PlayMethod.DirectPlay,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>()
            }
        };
    }

    private SqlitePlayStore OpenTheStore() => new(_root);

    /// <summary>
    /// A hold whose fold reads the store this case is writing into, counted on
    /// the way through. It is what lets a case assert both that the fold ran
    /// again and that what it said changed.
    /// </summary>
    private HeldYears OverTheStore(Counter folds)
    {
        return new HeldYears(
            (userId, year, zone, topCount) =>
            {
                folds.Fold(userId, year, zone, topCount);

                using var store = OpenTheStore();
                return YearInReview.Over(
                    store.PlaysFor(userId),
                    userId,
                    year,
                    zone,
                    topCount,
                    store.OldestPlayStartedUtc());
            },
            new FixedClock(Now));
    }

    /// <summary>
    /// A fold that counts how often it was asked and for whom. The whole of the
    /// first condition of issue #70 is a count of calls, and a count is not
    /// something a store can be asked for afterwards.
    /// </summary>
    private sealed class Counter
    {
        private readonly List<Guid> _asked = [];

        /// <summary>
        /// Gets how many times a year was folded.
        /// </summary>
        public int Calls => _asked.Count;

        /// <summary>
        /// Gets the accounts a fold was asked for, in the order they were asked.
        /// </summary>
        public IReadOnlyList<Guid> Asked => _asked;

        /// <summary>
        /// Records the call and answers an empty year.
        /// </summary>
        /// <param name="userId">Whose year was asked for.</param>
        /// <param name="year">The year that was asked for.</param>
        /// <param name="zone">The zone it was asked for in.</param>
        /// <param name="topCount">How many rows its top lists may hold.</param>
        /// <returns>An empty year, which is what a fold over no rows produces.</returns>
        public YearInReview Fold(Guid userId, int year, TimeZoneInfo zone, int topCount)
        {
            _asked.Add(userId);

            return YearInReview.Over(
                Array.Empty<PlayRecord>(),
                userId,
                year,
                zone,
                topCount,
                oldestPlayStartedUtc: null);
        }
    }
}
