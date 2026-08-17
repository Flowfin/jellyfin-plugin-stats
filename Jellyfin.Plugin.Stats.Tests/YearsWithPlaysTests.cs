// Which years a wrap-up's selector may offer, read out of a real store over a
// temporary directory. The condition this is written against is issue #67's
// third: the selector lists only years with data. The failure it exists against
// is the friendlier list somebody reaches for instead, every year from the
// oldest row to the one the server is in, which offers a quiet year and opens it
// empty.
//
// Nothing here reads a clock. Every moment is written down, because a suite that
// asks what year it is passes in December and fails on the first of January.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class YearsWithPlaysTests : IDisposable
{
    private static readonly Guid Watcher = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Somebody = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    // A zone that is behind UTC by five hours in winter, so a moment in the first
    // hours of the first of January belongs to the year before there. Named
    // rather than taken from the machine, which has whatever zone it has.
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private readonly string _root;

    public YearsWithPlaysTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// The years an account has rows in, ascending, and a year in the middle
    /// that it has none in is not one of them.
    /// </summary>
    /// <remarks>
    /// This is the condition and the failure in one case. A list derived from
    /// the oldest row and the newest would hold 2023 as well, and every one of
    /// this account's plays in 2023 is a play there are none of.
    /// </remarks>
    [Fact]
    public void OnlyTheYearsThatHaveRowsAreAnswered()
    {
        using var store = new SqlitePlayStore(_root);

        store.Add(APlay(Watcher, new DateTime(2022, 6, 1, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Watcher, new DateTime(2022, 9, 9, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Watcher, new DateTime(2024, 2, 2, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Watcher, new DateTime(2025, 11, 3, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal([2022, 2024, 2025], store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));
    }

    /// <summary>
    /// An account with nothing stored is answered with nothing, rather than with
    /// the year the server is in.
    /// </summary>
    /// <remarks>
    /// A selector handed one year it can open and find empty says the person
    /// watched nothing that year. An empty list says the plugin holds nothing of
    /// theirs at all, which is the true statement and the different one.
    /// </remarks>
    [Fact]
    public void AnAccountWithNoRowsIsAnsweredWithNoYears()
    {
        using var store = new SqlitePlayStore(_root);

        store.Add(APlay(Somebody, new DateTime(2025, 4, 4, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Empty(store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));
    }

    /// <summary>
    /// The years are the account's own. Another account watching through a year
    /// does not put that year in this one's list.
    /// </summary>
    /// <remarks>
    /// The page this feeds is the one a signed-in user opens about themselves,
    /// so a year offered because somebody else watched in it is a statement about
    /// that somebody made to a person who is not them.
    /// </remarks>
    [Fact]
    public void OneAccountsYearsAreNotAnothers()
    {
        using var store = new SqlitePlayStore(_root);

        store.Add(APlay(Watcher, new DateTime(2024, 5, 5, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Somebody, new DateTime(2021, 5, 5, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Somebody, new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal([2024], store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));
        Assert.Equal([2021, 2026], store.YearsWithPlaysFor(Somebody, TimeZoneInfo.Utc));
    }

    /// <summary>
    /// The zone decides which year a row falls in, and the same rows answer with
    /// different years in two zones.
    /// </summary>
    /// <remarks>
    /// One play, at half past four in the morning UTC on the first of January.
    /// In New York that is half past eleven the night before, so the year it
    /// belongs to is the one that has just ended. A store answering in UTC and
    /// leaving the caller to shift it afterwards would hand a selector a year
    /// this account has no plays in and hide the one it has.
    /// </remarks>
    [Fact]
    public void TheZoneDecidesWhichYearARowFallsIn()
    {
        using var store = new SqlitePlayStore(_root);

        store.Add(APlay(Watcher, new DateTime(2025, 1, 1, 4, 30, 0, DateTimeKind.Utc)));

        Assert.Equal([2025], store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));
        Assert.Equal([2024], store.YearsWithPlaysFor(Watcher, NewYork));
    }

    /// <summary>
    /// A year leaves the list when the last of its rows does.
    /// </summary>
    /// <remarks>
    /// Retention is the reason a year goes, and it is the reason the selector has
    /// to be read rather than kept. The sweep here is the store's own deletion by
    /// age, run against a cutoff, and what it leaves behind is what the selector
    /// may offer afterwards.
    /// </remarks>
    [Fact]
    public void AYearGoesWhenTheRowsUnderItAreSweptAway()
    {
        using var store = new SqlitePlayStore(_root);

        store.Add(APlay(Watcher, new DateTime(2023, 7, 7, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Watcher, new DateTime(2024, 7, 7, 12, 0, 0, DateTimeKind.Utc)));
        store.Add(APlay(Watcher, new DateTime(2025, 7, 7, 12, 0, 0, DateTimeKind.Utc)));

        Assert.Equal([2023, 2024, 2025], store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));

        store.DeletePlaysStartedBefore(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), 100);

        Assert.Equal([2025], store.YearsWithPlaysFor(Watcher, TimeZoneInfo.Utc));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// One play, with everything the store insists on and nothing it does not.
    /// What varies between the cases above is the account and the moment, so
    /// those are the arguments and the rest is fixed.
    /// </summary>
    /// <param name="userId">Whose play it is.</param>
    /// <param name="startedUtc">When it started, in UTC.</param>
    /// <returns>The row.</returns>
    private static PlayRecord APlay(Guid userId, DateTime startedUtc)
    {
        return new PlayRecord
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.AddMinutes(30),
            WatchedDuration = TimeSpan.FromMinutes(30),
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
}
