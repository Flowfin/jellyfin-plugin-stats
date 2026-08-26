// What a reason row counts, and the one thing it must never be read as.
//
// A reason row is a count of plays that recorded that reason, and the time
// those plays were watched for, and the rows are ordered by the second of the
// two. Beside them is what the server re-encoded with, which is a partition and
// is a separate list for exactly that reason. It is not a share of anything, because one play
// carries several reasons and the rows therefore add up to more than the plays
// and the minutes they came from. The failure these
// are written against is the opposite of the one the delivery figures guard:
// there a dropped play makes the answer too small, here a repeated reason on
// one row makes a single long film look like several plays. Every row is built
// in memory and no clock, zone or store is touched.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class TranscodeReasonBreakdownTests
{
    private static readonly string[] _serverReasons =
    {
        "ContainerNotSupported",
        "VideoCodecNotSupported",
        "AudioCodecNotSupported",
        "SubtitleCodecNotSupported",
        "DirectPlayError"
    };

    /// <summary>
    /// Every claim the type makes, over sequences nobody chose one at a time.
    /// Each row is checked against a count taken the other way round: the fold
    /// walks plays and accumulates reasons, and this walks each reason and
    /// counts the plays carrying it, so an accumulator that lost or double
    /// counted anything disagrees with it. The generator is seeded, so a
    /// failure is the same failure on the next run and on the runner.
    /// </summary>
    [Fact]
    public void EachRowCountsThePlaysThatRecordedItAndTheOrderIsSettledHere()
    {
        var generator = new Random(20260812);

        for (var sweep = 0; sweep < 500; sweep++)
        {
            var length = generator.Next(0, 120);
            var plays = new List<PlayRecord>(length);
            for (var i = 0; i < length; i++)
            {
                var reasons = new List<string>();
                foreach (var reason in _serverReasons)
                {
                    // Drawn per reason rather than as a set, so a play carries
                    // none, one or several, which is what a server produces.
                    if (generator.Next(4) == 0)
                    {
                        reasons.Add(reason);
                    }
                }

                // Varied per play, so a fold that used one play's watched time
                // for another, or that counted plays where it meant minutes,
                // comes out with the wrong sum rather than the same one.
                plays.Add(APlayWatchedFor(
                    TimeSpan.FromSeconds(generator.Next(0, 7_200)),
                    reasons.ToArray()));
            }

            var breakdown = TranscodeReasonBreakdown.Over(plays);

            Assert.Equal((long)length, breakdown.Plays);
            Assert.Equal(
                plays.Count(play => play.Transcode.Reasons.Count > 0),
                breakdown.PlaysWithAtLeastOneReason);
            Assert.Equal(
                breakdown.Reasons.Select(row => row.Reason).Distinct(StringComparer.Ordinal).Count(),
                breakdown.Reasons.Count);

            Assert.Equal(
                TimeSpan.FromTicks(plays.Sum(play => play.WatchedDuration.Ticks)).TotalMinutes,
                breakdown.WatchedMinutes);

            // The acceleration rows are a partition and the reason rows are
            // not, which is the property that separates the two lists. A fold
            // that put a play under two accelerations, or dropped one, breaks
            // this and leaves the reason rows untouched.
            Assert.Equal((long)length, breakdown.Acceleration.Sum(row => row.Plays));
            Assert.Equal(
                breakdown.WatchedMinutes,
                breakdown.Acceleration.Sum(row => row.WatchedMinutes),
                8);
            Assert.Equal(
                TimeSpan.FromTicks(plays
                    .Where(play => play.Transcode.Reasons.Count > 0)
                    .Sum(play => play.WatchedDuration.Ticks)).TotalMinutes,
                breakdown.WatchedMinutesWithAtLeastOneReason);

            foreach (var row in breakdown.Reasons)
            {
                Assert.Equal(
                    plays.Count(play => play.Transcode.Reasons.Contains(row.Reason, StringComparer.Ordinal)),
                    row.Plays);
                Assert.InRange(row.Plays, 1, breakdown.PlaysWithAtLeastOneReason);

                // Counted the other way round as well: every play carrying this
                // reason contributes the whole of its watched time, so a fold
                // that apportioned or that lost a play disagrees here.
                Assert.Equal(
                    TimeSpan.FromTicks(plays
                        .Where(play => play.Transcode.Reasons.Contains(row.Reason, StringComparer.Ordinal))
                        .Sum(play => play.WatchedDuration.Ticks)).TotalMinutes,
                    row.WatchedMinutes);
                Assert.InRange(
                    row.WatchedMinutes,
                    0,
                    breakdown.WatchedMinutesWithAtLeastOneReason);
            }

            for (var i = 1; i < breakdown.Reasons.Count; i++)
            {
                var earlier = breakdown.Reasons[i - 1];
                var later = breakdown.Reasons[i];

                Assert.True(
                    earlier.WatchedMinutes > later.WatchedMinutes
                    || (earlier.WatchedMinutes == later.WatchedMinutes
                        && (earlier.Plays > later.Plays
                            || (earlier.Plays == later.Plays
                                && string.CompareOrdinal(earlier.Reason, later.Reason) < 0))),
                    "Rows are ordered by watched time, then by plays, then by the server's own "
                    + "name, and " + earlier.Reason + " came before " + later.Reason + ".");
            }
        }
    }

    /// <summary>
    /// The sentence <c>docs/transcode-reasons.md</c> exists for, as arithmetic
    /// rather than as prose. Three plays, each carrying two reasons, produce
    /// rows totalling six. A reader meeting six and three on one page without
    /// the document concludes the plugin is counting wrong, and the document is
    /// only worth reading while this is what the fold does.
    /// </summary>
    [Fact]
    public void TheRowsTotalMoreThanThePlaysWhereAPlayRecordedSeveralReasons()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayReporting("ContainerNotSupported", "AudioCodecNotSupported"),
            APlayReporting("ContainerNotSupported", "AudioCodecNotSupported"),
            APlayReporting("ContainerNotSupported", "AudioCodecNotSupported")
        });

        Assert.Equal(3, breakdown.Plays);
        Assert.Equal(3, breakdown.PlaysWithAtLeastOneReason);
        Assert.Equal(6, breakdown.Reasons.Sum(row => row.Plays));
    }

    /// <summary>
    /// One play carrying four reasons puts the whole of its watched time under
    /// each of the four. This is the decision of 2026-08-24 on issue #242 as
    /// arithmetic: every minute on a row is a minute somebody watched while
    /// that condition held, and a quarter of a play apportioned four ways is a
    /// figure nobody watched and nothing can be checked against. The cost is
    /// the sum, which is four times the period here and is the reason the view
    /// carries a sentence about it.
    /// </summary>
    [Fact]
    public void OnePlaysWatchedTimeIsCountedInFullUnderEveryReasonItCarries()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayWatchedFor(
                TimeSpan.FromMinutes(90),
                "ContainerNotSupported",
                "VideoCodecNotSupported",
                "AudioCodecNotSupported",
                "SubtitleCodecNotSupported")
        });

        Assert.Equal(4, breakdown.Reasons.Count);
        Assert.All(breakdown.Reasons, row => Assert.Equal(90, row.WatchedMinutes));

        // The period is ninety minutes and the rows total three hundred and
        // sixty. Both statements are true at once, which is what the page has
        // to say out loud rather than leave a reader to reconcile.
        Assert.Equal(90, breakdown.WatchedMinutes);
        Assert.Equal(90, breakdown.WatchedMinutesWithAtLeastOneReason);
        Assert.Equal(360, breakdown.Reasons.Sum(row => row.WatchedMinutes));
    }

    /// <summary>
    /// A play watched for no time is a row rather than an absence. The server
    /// gave its reasons and the play is one of the plays, so dropping it would
    /// make the play count and the row disagree for a reason that has nothing
    /// to do with what was watched.
    /// </summary>
    [Fact]
    public void APlayWatchedForNoTimeStillCountsAsAPlayUnderItsReasons()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayWatchedFor(TimeSpan.Zero, "ContainerNotSupported")
        });

        var row = Assert.Single(breakdown.Reasons);
        Assert.Equal(1, row.Plays);
        Assert.Equal(0, row.WatchedMinutes);
        Assert.Equal(1, breakdown.PlaysWithAtLeastOneReason);
    }

    /// <summary>
    /// A stored row repeating a reason is counted once. The capture fold drops
    /// the repeat when it collects the reasons, so a row this build wrote never
    /// looks like this; a row another build wrote is still read by this one,
    /// and counting the repeat would report one play as two under that reason
    /// while the play count beside it stayed at one.
    /// </summary>
    [Fact]
    public void APlayIsCountedOnceUnderAReasonHoweverOftenTheStoredRowRepeatsIt()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayReporting("VideoCodecNotSupported", "VideoCodecNotSupported", "VideoCodecNotSupported")
        });

        var row = Assert.Single(breakdown.Reasons);
        Assert.Equal("VideoCodecNotSupported", row.Reason);
        Assert.Equal(1, row.Plays);
        Assert.Equal(38, row.WatchedMinutes);
        Assert.Equal(1, breakdown.Plays);
        Assert.Equal(1, breakdown.PlaysWithAtLeastOneReason);
        Assert.Equal(38, breakdown.WatchedMinutesWithAtLeastOneReason);
    }

    /// <summary>
    /// The same plays in a different order are the same answer. A query returns
    /// rows in whatever order its plan produced, and a breakdown whose order
    /// followed that would draw a chart whose bars move when an index changes.
    /// Every play here is the same length, so watched time and plays rank the
    /// rows the same way and the two reasons level on both are settled by the
    /// server's own name rather than by which arrived first.
    /// </summary>
    [Fact]
    public void TheOrderDoesNotFollowTheOrderThePlaysArrivedIn()
    {
        var plays = new[]
        {
            APlayReporting("VideoCodecNotSupported", "DirectPlayError"),
            APlayReporting("VideoCodecNotSupported", "AudioCodecNotSupported"),
            APlayReporting("VideoCodecNotSupported", "AudioCodecNotSupported")
        };

        var forwards = TranscodeReasonBreakdown.Over(plays);
        var backwards = TranscodeReasonBreakdown.Over(plays.Reverse());

        Assert.Equal(
            new[] { "VideoCodecNotSupported", "AudioCodecNotSupported", "DirectPlayError" },
            forwards.Reasons.Select(row => row.Reason));
        Assert.Equal(
            forwards.Reasons.Select(row => row.Reason),
            backwards.Reasons.Select(row => row.Reason));
        Assert.Equal(
            new long[] { 3, 2, 1 },
            forwards.Reasons.Select(row => row.Plays));
    }

    /// <summary>
    /// The order is the watched time and not the play count, which is issue
    /// #60's description. Four hundred one-minute plays under one reason and
    /// four two-hour plays under another: the count says the first is a hundred
    /// times the problem, the watched time says the second is the larger one,
    /// and only the second is a server spending its evening re-encoding. Both
    /// counts are still carried, because the two readings disagree and that is
    /// the part worth seeing.
    /// </summary>
    [Fact]
    public void ARareReasonThatCostHoursOutranksACommonOneThatCostMinutes()
    {
        var plays = new List<PlayRecord>();
        for (var i = 0; i < 400; i++)
        {
            plays.Add(APlayWatchedFor(TimeSpan.FromMinutes(1), "ContainerNotSupported"));
        }

        for (var i = 0; i < 4; i++)
        {
            plays.Add(APlayWatchedFor(TimeSpan.FromHours(2), "VideoCodecNotSupported"));
        }

        var breakdown = TranscodeReasonBreakdown.Over(plays);

        Assert.Equal(
            new[] { "VideoCodecNotSupported", "ContainerNotSupported" },
            breakdown.Reasons.Select(row => row.Reason));
        Assert.Equal(new long[] { 4, 400 }, breakdown.Reasons.Select(row => row.Plays));
        Assert.Equal(new double[] { 480, 400 }, breakdown.Reasons.Select(row => row.WatchedMinutes));
    }

    /// <summary>
    /// What the server re-encoded with, as a partition of the plays folded. The
    /// second half of issue #60's description asks for it beside the reasons,
    /// and it is a separate list because a play carries one acceleration and
    /// every reason the server gave, so one of the two lists adds up to the
    /// plays and the other does not.
    /// </summary>
    [Fact]
    public void TheAccelerationRowsAddUpToThePlaysAndTheReasonRowsDoNot()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayAcceleratedBy("qsv", TimeSpan.FromMinutes(60), "ContainerNotSupported", "VideoCodecNotSupported"),
            APlayAcceleratedBy("qsv", TimeSpan.FromMinutes(30), "ContainerNotSupported"),
            APlayAcceleratedBy("vaapi", TimeSpan.FromMinutes(20), "ContainerNotSupported")
        });

        Assert.Equal(
            new[] { "qsv", "vaapi" },
            breakdown.Acceleration.Select(row => row.Type));
        Assert.Equal(new long[] { 2, 1 }, breakdown.Acceleration.Select(row => row.Plays));
        Assert.Equal(new double[] { 90, 20 }, breakdown.Acceleration.Select(row => row.WatchedMinutes));

        Assert.Equal(3, breakdown.Acceleration.Sum(row => row.Plays));
        Assert.Equal(110, breakdown.Acceleration.Sum(row => row.WatchedMinutes));
        Assert.Equal(4, breakdown.Reasons.Sum(row => row.Plays));
    }

    /// <summary>
    /// A play the server reported no acceleration for is a row of its own and
    /// it is last. Naming that group software would be a claim the row cannot
    /// carry: this fold reads the summary rather than the delivery method, so a
    /// play that was passed through untouched and a play re-encoded on the
    /// processor arrive here as the same absence, which is issue #158.
    /// </summary>
    [Fact]
    public void ThePlaysWithNoReportedAccelerationAreTheirOwnRowAndComeLast()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayAcceleratedBy(null, TimeSpan.FromHours(9), "ContainerNotSupported"),
            APlayAcceleratedBy("qsv", TimeSpan.FromMinutes(1), "ContainerNotSupported")
        });

        Assert.Equal(new[] { "qsv", null }, breakdown.Acceleration.Select(row => row.Type));
        Assert.Equal(2, breakdown.Acceleration.Sum(row => row.Plays));
    }

    /// <summary>
    /// A play that recorded no reason is in the play count and under no row,
    /// and no row is invented for it. Most such plays were passed through and
    /// needed none; this fold reads the summary rather than the delivery
    /// method, so it cannot tell those from a play that was re-encoded and
    /// reported nothing, and a row saying otherwise would be a guess.
    /// </summary>
    [Fact]
    public void APlayThatRecordedNoReasonIsInThePlayCountAndUnderNoRow()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayReporting("ContainerNotSupported"),
            APlayReporting()
        });

        var row = Assert.Single(breakdown.Reasons);
        Assert.Equal("ContainerNotSupported", row.Reason);
        Assert.Equal(1, row.Plays);
        Assert.Equal(2, breakdown.Plays);
        Assert.Equal(1, breakdown.PlaysWithAtLeastOneReason);

        // The play with no reason is in the period and under no row, so the two
        // totals differ by exactly it. A fold that left it out of the period
        // would make the rows look like a division of the range after all.
        Assert.Equal(76, breakdown.WatchedMinutes);
        Assert.Equal(38, breakdown.WatchedMinutesWithAtLeastOneReason);
    }

    /// <summary>
    /// Names are compared as bytes, which is the comparer the capture fold
    /// already dedupes with. Two spellings are two observations, and folding
    /// them together would be the plugin deciding they mean the same thing.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOneNameAreTwoRows()
    {
        var breakdown = TranscodeReasonBreakdown.Over(new[]
        {
            APlayReporting("DirectPlayError", "directplayerror")
        });

        Assert.Equal(2, breakdown.Reasons.Count);
        Assert.Equal(1, breakdown.PlaysWithAtLeastOneReason);
    }

    /// <summary>
    /// A range with no plays in it answers with no rows and a play count of
    /// nought, rather than with nothing, so a caller does not have to tell an
    /// empty answer from an absent one.
    /// </summary>
    [Fact]
    public void AnEmptyRangeIsNoRowsRatherThanNoAnswer()
    {
        var breakdown = TranscodeReasonBreakdown.Over(Array.Empty<PlayRecord>());

        Assert.Empty(breakdown.Reasons);
        Assert.Empty(breakdown.Acceleration);
        Assert.Equal(0, breakdown.Plays);
        Assert.Equal(0, breakdown.PlaysWithAtLeastOneReason);
        Assert.Equal(0, breakdown.WatchedMinutes);
        Assert.Equal(0, breakdown.WatchedMinutesWithAtLeastOneReason);
    }

    [Fact]
    public void AMissingSequenceIsRefusedRatherThanReadAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => TranscodeReasonBreakdown.Over(null!));
    }

    private static PlayRecord APlayReporting(params string[] reasons)
        => APlayWatchedFor(TimeSpan.FromMinutes(38), reasons);

    private static PlayRecord APlayWatchedFor(TimeSpan watched, params string[] reasons)
        => APlayAcceleratedBy(null, watched, reasons);

    private static PlayRecord APlayAcceleratedBy(
        string? acceleration,
        TimeSpan watched,
        params string[] reasons) => new()
    {
        SchemaVersion = 1,
        UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Episode",
        ParentId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
        ItemName = "An episode",
        ItemRuntime = TimeSpan.FromMinutes(42),
        ChannelName = null,
        StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
        EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
        WatchedDuration = watched,
        ReachedTheEnd = true,
        ClientName = "Jellyfin Web",
        DeviceId = "device-1",
        DeviceName = "A browser",
        PlayMethodAtStart = reasons.Length == 0 ? PlayMethod.DirectPlay : PlayMethod.Transcode,
        PlayMethodChangedUtc = null,
        ClosedBy = PlayClosedBy.AStopEvent,
        Transcode = new TranscodeSummary
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            VideoWasDirect = reasons.Length == 0,
            AudioWasDirect = reasons.Length == 0,
            PeakBitrate = null,
            TypicalBitrate = null,
            HardwareAcceleration = acceleration,
            Reasons = reasons
        }
    };
}
