// Reports over items the library no longer has, and over items this account may
// not see. Issue #54.
//
// The two are opposite cases that a report answers with the same field, which is
// why they are written together. An item that has been deleted is one nobody can
// be shown and everybody's plays of it are still theirs, so it is named out of
// the row the way every other label here is; an item the library still holds and
// this account may not see is a name that would be telling them something the
// server has decided not to. A report that collapsed the two would either empty
// itself of everything anybody ever deleted or hand out the names it exists to
// withhold.
//
// Both are read against a library that changes between the write and the read,
// because that is the only sequence in which either failure happens. A fold
// against a fixed library proves the arithmetic and nothing about access.
//
// Nothing here touches a store, a clock or a server. The library is the fake in
// Fakes/FakeItemAccess.cs, which is a set of declarations rather than a stand-in
// for an interface carrying a hundred methods.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class TopListsAndItemAccessTests
{
    private static readonly Guid Mine = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// The first condition of issue #54, over a library that lets go of the item
    /// between the write and the read. Every play is still counted and the row
    /// is still named, out of what the row recorded rather than out of a library
    /// that has nothing left to say about it.
    /// </summary>
    [Fact]
    public void APlayOfADeletedItemIsStillCountedAndStillNamed()
    {
        var deleted = AnIdentifier(1);
        var kept = AnIdentifier(2);

        var plays = new List<PlayRecord>
        {
            APlay(itemId: deleted, itemName: "A film nobody kept", watched: TimeSpan.FromMinutes(90)),
            APlay(itemId: kept, itemName: "A film still there", watched: TimeSpan.FromMinutes(30))
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);

        // The library held both when the plays were written and lets go of one
        // of them afterwards, which is the sequence this condition is about.
        var library = FakeItemAccess.EverythingVisible.LetGoOf(deleted);

        var read = folded.SeenBy(Mine, library);

        Assert.Equal(2, read.TopItems.Count);
        Assert.Equal("A film nobody kept", read.TopItems[0].Name);
        Assert.Equal(TimeSpan.FromMinutes(90), read.TopItems[0].Watched);
        Assert.Equal(2L, read.Plays);
        Assert.Equal(2L, read.DistinctItems);
        Assert.Equal(TimeSpan.FromMinutes(120), read.Watched);
    }

    /// <summary>
    /// The second condition of issue #54. The item is one the library still
    /// holds and this account may not see, so the name is withheld, and the
    /// figures the issue says it still counts under are asserted beside it: a
    /// report that had dropped the play from its totals would be answering a
    /// different question from the one asked.
    /// </summary>
    [Fact]
    public void AnItemThisAccountMayNotSeeIsNeverNamedAndIsStillCounted()
    {
        var withheld = AnIdentifier(1);
        var visible = AnIdentifier(2);

        var plays = new List<PlayRecord>
        {
            APlay(itemId: withheld, itemName: "Not for this account", watched: TimeSpan.FromMinutes(90)),
            APlay(itemId: visible, itemName: "For this account", watched: TimeSpan.FromMinutes(30))
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);

        var read = folded.SeenBy(Mine, FakeItemAccess.EverythingVisible.Withholding(Mine, withheld));

        Assert.Equal(new[] { visible }, read.TopItems.Select(row => row.Key));
        Assert.DoesNotContain(read.TopItems, row => row.Name == "Not for this account");

        Assert.Equal(2L, read.Plays);
        Assert.Equal(2L, read.DistinctItems);
        Assert.Equal(TimeSpan.FromMinutes(120), read.Watched);
        Assert.Equal(TimeSpan.FromMinutes(90), read.LongestPlay);
    }

    /// <summary>
    /// The third condition of issue #54: one case that deletes items between the
    /// write and the read and asserts both of the others over the same answer.
    /// The library holds three items at the moment the plays are folded, and by
    /// the time the year is read it has let go of one and is withholding
    /// another.
    /// </summary>
    [Fact]
    public void ALibraryThatChangesBetweenTheWriteAndTheReadAnswersBothWays()
    {
        var deleted = AnIdentifier(1);
        var withheld = AnIdentifier(2);
        var visible = AnIdentifier(3);

        var plays = new List<PlayRecord>
        {
            APlay(itemId: deleted, itemName: "Deleted since", watched: TimeSpan.FromMinutes(90)),
            APlay(itemId: withheld, itemName: "Withheld since", watched: TimeSpan.FromMinutes(60)),
            APlay(itemId: visible, itemName: "Still shown", watched: TimeSpan.FromMinutes(30))
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);

        // Everything was visible while the plays were being written. What the
        // two lines below describe is the library afterwards.
        var libraryNow = FakeItemAccess.EverythingVisible
            .LetGoOf(deleted)
            .Withholding(Mine, withheld);

        var read = folded.SeenBy(Mine, libraryNow);

        Assert.Equal(new[] { deleted, visible }, read.TopItems.Select(row => row.Key));
        Assert.Equal(new[] { "Deleted since", "Still shown" }, read.TopItems.Select(row => row.Name));
        Assert.Equal(3L, read.Plays);
        Assert.Equal(3L, read.DistinctItems);
        Assert.Equal(TimeSpan.FromMinutes(180), read.Watched);
    }

    /// <summary>
    /// Why the fold keeps its lists whole. Twelve items and a bound of ten, with
    /// two of the top ten withheld: the answer is ten rows with the eleventh and
    /// twelfth moved up, and not eight. A fold that cut to the bound before the
    /// access question was asked could only give the eight, and eight rows under
    /// a heading that says ten is a correct answer to a question nobody asked.
    /// </summary>
    [Fact]
    public void AWithheldRowIsReplacedRatherThanLeavingAShorterList()
    {
        var plays = new List<PlayRecord>();
        for (var rank = 0; rank < 12; rank++)
        {
            // Descending watched time, so the identifier's number is also the
            // row's position in the ranked list and the assertions below can be
            // read without recomputing the order.
            plays.Add(APlay(
                itemId: AnIdentifier(rank),
                itemName: "Item " + rank.ToString(CultureInfo.InvariantCulture),
                watched: TimeSpan.FromMinutes(120 - rank)));
        }

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);
        Assert.Equal(10, folded.TopItems.Count);

        var read = folded.SeenBy(
            Mine,
            FakeItemAccess.EverythingVisible
                .Withholding(Mine, AnIdentifier(2))
                .Withholding(Mine, AnIdentifier(5)));

        Assert.Equal(10, read.TopItems.Count);
        Assert.Equal(
            new[] { 0, 1, 3, 4, 6, 7, 8, 9, 10, 11 }.Select(AnIdentifier),
            read.TopItems.Select(row => row.Key));
    }

    /// <summary>
    /// The library is asked about the rows a list needs and not about the year.
    /// The walk stops as soon as the list is full, so a year of a hundred items
    /// answered as a top ten costs about ten questions and not a hundred, which
    /// is what makes asking per request rather than per fold affordable.
    /// </summary>
    [Fact]
    public void TheLibraryIsAskedAboutTheListRatherThanAboutTheYear()
    {
        var plays = new List<PlayRecord>();
        for (var rank = 0; rank < 100; rank++)
        {
            plays.Add(APlay(
                itemId: AnIdentifier(rank),
                itemName: "Item " + rank.ToString(CultureInfo.InvariantCulture),
                watched: TimeSpan.FromMinutes(200 - rank)));
        }

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);
        var library = FakeItemAccess.EverythingVisible;

        var read = folded.SeenBy(Mine, library);

        Assert.Equal(10, read.TopItems.Count);
        Assert.Equal(100L, read.DistinctItems);

        // Ten for the item list and nothing for the series list, because no play
        // above carries a parent. The bound is what is being asserted rather
        // than the exact number: a walk over the whole year would be a hundred.
        Assert.Equal(10, library.TimesAsked);
    }

    /// <summary>
    /// The series list is cut on the same rule, although no row in it carries a
    /// name today. A row here says this account watched something under a parent
    /// they may no longer see, and the day a stored series name reaches the row
    /// it starts printing one.
    /// </summary>
    [Fact]
    public void TheSeriesListIsCutOnTheSameRule()
    {
        var withheldSeries = AnIdentifier(101);
        var shownSeries = AnIdentifier(102);

        var plays = new List<PlayRecord>
        {
            APlay(itemId: AnIdentifier(1), parentId: withheldSeries, watched: TimeSpan.FromMinutes(90)),
            APlay(itemId: AnIdentifier(2), parentId: shownSeries, watched: TimeSpan.FromMinutes(30))
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);
        Assert.Equal(2, folded.TopSeries.Count);

        var read = folded.SeenBy(Mine, FakeItemAccess.EverythingVisible.Withholding(Mine, withheldSeries));

        Assert.Equal(new[] { shownSeries }, read.TopSeries.Select(row => row.Key));
        Assert.Equal(2L, read.Plays);
    }

    /// <summary>
    /// A year with nothing in it is handed back as it was, and the library is
    /// asked nothing at all. There is no list to cut and no item to ask about,
    /// and a copy of it would be a second object saying what the first one says.
    /// </summary>
    [Fact]
    public void AYearWithNothingInItIsUnchangedAndCostsNoQuestion()
    {
        var folded = YearInReview.Over(
            Array.Empty<PlayRecord>(),
            Mine,
            2026,
            Berlin,
            10,
            oldestPlayStartedUtc: null);

        var library = FakeItemAccess.HoldingNothing;
        var read = folded.SeenBy(Mine, library);

        Assert.False(read.AnythingRecorded);
        Assert.Same(folded, read);
        Assert.Equal(0, library.TimesAsked);
    }

    /// <summary>
    /// Every figure other than the two lists is what the fold said it was. The
    /// copy is a copy rather than a second fold, so a total read off a filtered
    /// answer and a total read off the answer it came from cannot drift.
    /// </summary>
    [Fact]
    public void NothingButTheTwoListsMoves()
    {
        var plays = new List<PlayRecord>
        {
            APlay(itemId: AnIdentifier(1), parentId: AnIdentifier(101), watched: TimeSpan.FromMinutes(90), reachedTheEnd: true),
            APlay(itemId: AnIdentifier(2), watched: TimeSpan.FromMinutes(30), reachedTheEnd: false)
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);
        var read = folded.SeenBy(Mine, FakeItemAccess.EverythingVisible.Withholding(Mine, AnIdentifier(1)));

        Assert.Equal(folded.Year, read.Year);
        Assert.Equal(folded.ZoneId, read.ZoneId);
        Assert.Equal(folded.Coverage, read.Coverage);
        Assert.Equal(folded.AnythingRecorded, read.AnythingRecorded);
        Assert.Equal(folded.Plays, read.Plays);
        Assert.Equal(folded.Watched, read.Watched);
        Assert.Equal(folded.DistinctItems, read.DistinctItems);
        Assert.Equal(folded.LongestPlay, read.LongestPlay);
        Assert.Equal(folded.BusiestDay, read.BusiestDay);
        Assert.Equal(folded.BusiestMonth, read.BusiestMonth);
        Assert.Equal(folded.Finished, read.Finished);
        Assert.Equal(folded.Abandoned, read.Abandoned);
        Assert.Equal(folded.Delivery, read.Delivery);
    }

    /// <summary>
    /// Reading the same folded year twice for two accounts gives each of them
    /// their own answer, and neither reading changes the other. This is what
    /// holding the tallies uncut buys over putting the access answer into what a
    /// held year is filed under.
    /// </summary>
    [Fact]
    public void TwoAccountsReadOneFoldedYearAndGetTheirOwnLists()
    {
        var other = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000001");
        var one = AnIdentifier(1);
        var two = AnIdentifier(2);

        var plays = new List<PlayRecord>
        {
            APlay(itemId: one, watched: TimeSpan.FromMinutes(90)),
            APlay(itemId: two, watched: TimeSpan.FromMinutes(30))
        };

        var folded = YearInReview.Over(plays, Mine, 2026, Berlin, 10, oldestPlayStartedUtc: null);

        var library = FakeItemAccess.EverythingVisible.Withholding(other, one);

        var mine = folded.SeenBy(Mine, library);
        var theirs = folded.SeenBy(other, library);
        var mineAgain = folded.SeenBy(Mine, library);

        Assert.Equal(new[] { one, two }, mine.TopItems.Select(row => row.Key));
        Assert.Equal(new[] { two }, theirs.TopItems.Select(row => row.Key));
        Assert.Equal(new[] { one, two }, mineAgain.TopItems.Select(row => row.Key));
        Assert.Equal(2, folded.TopItems.Count);
    }

    /// <summary>
    /// The library is not something a caller may leave out.
    /// </summary>
    [Fact]
    public void ReadingAYearWithoutALibraryIsRefused()
    {
        var folded = YearInReview.Over(
            new[] { APlay(itemId: AnIdentifier(1)) },
            Mine,
            2026,
            Berlin,
            10,
            oldestPlayStartedUtc: null);

        Assert.Throws<ArgumentNullException>(() => folded.SeenBy(Mine, access: null!));
    }

    private static Guid AnIdentifier(int seed) =>
        new Guid(seed, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]);

    private static DateTime Noon => new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private static PlayRecord APlay(
        Guid? itemId = null,
        Guid? parentId = null,
        string itemName = "An episode",
        TimeSpan? watched = null,
        bool reachedTheEnd = true) => new()
        {
            SchemaVersion = 1,
            UserId = Mine,
            ItemId = itemId ?? AnIdentifier(1),
            ItemType = "Episode",
            ParentId = parentId,
            ItemName = itemName,
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = Noon,
            EndedUtc = Noon.AddMinutes(41),
            WatchedDuration = watched ?? TimeSpan.FromMinutes(38),
            ReachedTheEnd = reachedTheEnd,
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
