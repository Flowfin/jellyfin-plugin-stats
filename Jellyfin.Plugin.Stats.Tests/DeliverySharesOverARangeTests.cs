// The first condition of issue #53, asserted where the condition puts it: over
// a range.
//
// A neighbouring file already holds the same property over the fold, given a
// list of plays somebody handed it. That proves the counting and says nothing
// about which plays were counted. Between a range and a figure sit the window's
// two bounds and a statement that fetches rows out of a file, and a play the
// range holds but the read never returned is a play missing from an answer with
// nothing on the answer saying so. So this drives it through the query layer
// over a real store, and computes what each range holds separately from what
// the layer says it holds.
//
// The rows are written once and the windows are drawn over them, so the sweep
// costs one store rather than one per window. Every moment is chosen here and
// nothing reads a clock.
//
// What this does not reach, and both are measured rather than supposed.
//
// A range holding more plays than the window's bound allows. There the four
// figures add up to what was read rather than to what the range holds, the
// answer is not marked as truncated, and that gap belongs to issue #56 rather
// than being covered or contradicted here. Every window below stays well inside
// the bound.
//
// And a total taken as the sum of the four rather than counted. Replacing the
// range's play count with those four added together leaves every case here
// green, because the fold counts each play exactly once and the two agree. It
// is the arithmetic that makes the property true by definition, which is why
// the count is taken separately where it is taken, and the sentence at
// DeliveryMethodShares.Plays is what says so. Seeing it would need a fold that
// can lose a play as well, and that is two changes rather than the one-character
// mistake these cases are written against.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class DeliverySharesOverARangeTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly Guid AFilm = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime March = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The six values a stored row can carry. Two of them are outside the set
    /// this build names, so the sweep covers the row a later build wrote as
    /// well as the four this one understands. The store keeps the number rather
    /// than the name, so those two survive the write and come back unnamed.
    /// </summary>
    private static readonly PlayMethod[] Methods =
    {
        PlayMethod.Unknown,
        PlayMethod.DirectPlay,
        PlayMethod.DirectStream,
        PlayMethod.Transcode,
        (PlayMethod)7,
        (PlayMethod)(-3)
    };

    private readonly string _root;

    public DeliverySharesOverARangeTests()
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
    /// Over ranges nobody chose one at a time, the four figures a range answers
    /// with add up to the plays that range holds, and each of the four holds the
    /// plays that belong under it.
    /// </summary>
    /// <remarks>
    /// The expected figures are folded here from the rows this case wrote and
    /// the window's own half-open rule, rather than from anything the layer
    /// returned. That is what makes this a statement about the range: a read
    /// that took a play the window excludes, or dropped one it includes, moves
    /// the expectation and the answer apart, where a case comparing the answer
    /// against itself would pass.
    /// <para>
    /// The generator is seeded, so a failure is the same failure on the next run
    /// and on the runner. An unseeded one would report a defect that cannot be
    /// reproduced from the output.
    /// </para>
    /// <para>
    /// The windows are drawn to land on both sides of the rows as well as inside
    /// them, so ranges that hold nothing and ranges that hold everything are in
    /// the sweep rather than being the cases nobody generated.
    /// </para>
    /// <para>
    /// Half of the bounds are the start moment of a row rather than a moment
    /// drawn at large, and that is the difference between a sweep that watches
    /// the range and one that only watches the counting. A window is half open,
    /// so a play starting exactly at its end is outside it; where no bound ever
    /// coincides with a row, an end that included that play is a change no
    /// generated window can see. Drawing the bounds off the rows was added after
    /// a run in which closing the window at its top end left this case green.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheFourFiguresARangeAnswersWithAddUpToThePlaysThatRangeHolds()
    {
        var generator = new Random(20260823);
        var plays = new List<PlayRecord>();

        for (var i = 0; i < 400; i++)
        {
            plays.Add(APlayDeliveredBy(
                Methods[generator.Next(Methods.Length)],
                March.AddMinutes(generator.Next(0, 60 * 24 * 30)),
                i % 2 == 0 ? Alice : Bob));
        }

        Store(plays);

        var queries = new AggregateQueries(OpenTheStore);

        for (var sweep = 0; sweep < 200; sweep++)
        {
            var from = ABound(generator, plays);
            var to = ABound(generator, plays);

            if (to < from)
            {
                (from, to) = (to, from);
            }

            var window = QueryWindow.Of(from, to);

            var held = plays.Where(play => play.StartedUtc >= from && play.StartedUtc < to).ToList();
            var totals = queries.Total(window);
            var shares = totals.Delivery;

            Assert.Equal((long)held.Count, totals.Plays);
            Assert.Equal(totals.Plays, shares.Plays);
            Assert.Equal(
                shares.Plays,
                shares.Unknown + shares.DirectPlay + shares.DirectStream + shares.Transcode);

            Assert.Equal(Counted(held, PlayMethod.DirectPlay), shares.DirectPlay);
            Assert.Equal(Counted(held, PlayMethod.DirectStream), shares.DirectStream);
            Assert.Equal(Counted(held, PlayMethod.Transcode), shares.Transcode);
            Assert.Equal(
                held.Count - Counted(held, PlayMethod.DirectPlay) - Counted(held, PlayMethod.DirectStream) - Counted(held, PlayMethod.Transcode),
                shares.Unknown);
        }
    }

    /// <summary>
    /// The decision of 2026-08-09 on issue #53, over a range and through the
    /// store rather than over a list. A play the server never reported a method
    /// for, and a play carrying a method this build has no name for, are both
    /// reported as unknown, and the direct figures stay at nought.
    /// </summary>
    /// <remarks>
    /// The sweep above would catch either of them going elsewhere, and this says
    /// which failure it is in one reading. It is also the case that proves the
    /// unnamed value survives the round trip: the store keeps the number, so a
    /// row a later build wrote comes back as a method this one cannot name
    /// rather than as one it can.
    /// </remarks>
    [Fact]
    public void ARangeCountsWhatItCannotNameAsUnknownAndNeverAsDirect()
    {
        Store(new[]
        {
            APlayDeliveredBy(PlayMethod.Unknown, March, Alice),
            APlayDeliveredBy((PlayMethod)7, March.AddHours(1), Bob),
            APlayDeliveredBy(PlayMethod.DirectPlay, March.AddHours(2), Alice)
        });

        var shares = new AggregateQueries(OpenTheStore)
            .Total(QueryWindow.Of(March, March.AddDays(1)))
            .Delivery;

        Assert.Equal(2, shares.Unknown);
        Assert.Equal(1, shares.DirectPlay);
        Assert.Equal(0, shares.DirectStream);
        Assert.Equal(0, shares.Transcode);
        Assert.Equal(3, shares.Plays);
    }

    /// <summary>
    /// One end of a window: half the time the moment a stored play started, and
    /// half the time a moment drawn from the span the rows lie in and the
    /// stretches either side of it.
    /// </summary>
    /// <remarks>
    /// The first half is what puts a boundary exactly where a play is, which is
    /// the only place the half-open rule can be seen. The second is what puts
    /// ranges holding nothing and ranges holding everything into the sweep.
    /// </remarks>
    /// <param name="generator">The seeded generator.</param>
    /// <param name="plays">The rows the bounds are drawn against.</param>
    /// <returns>A moment in UTC.</returns>
    private static DateTime ABound(Random generator, IReadOnlyList<PlayRecord> plays)
        => generator.Next(2) == 0
            ? plays[generator.Next(plays.Count)].StartedUtc
            : March.AddMinutes(generator.Next(-2000, (60 * 24 * 30) + 2000));

    private static long Counted(IEnumerable<PlayRecord> plays, PlayMethod method)
        => plays.Count(play => play.PlayMethodAtStart == method);

    private static PlayRecord APlayDeliveredBy(PlayMethod method, DateTime startedUtc, Guid userId)
    {
        var length = TimeSpan.FromMinutes(40);

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
            EndedUtc = startedUtc + length,
            WatchedDuration = length,
            ReachedTheEnd = false,
            ClientName = "Jellyfin Web",
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
                Reasons = Array.Empty<string>()
            }
        };
    }

    private SqlitePlayStore OpenTheStore() => new(_root);

    private void Store(IEnumerable<PlayRecord> plays)
    {
        using var store = new SqlitePlayStore(_root);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }
}
