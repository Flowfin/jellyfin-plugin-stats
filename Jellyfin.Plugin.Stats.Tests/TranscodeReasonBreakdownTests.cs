// What a reason row counts, and the one thing it must never be read as.
//
// A reason row is a count of plays that recorded that reason. It is not a
// share of anything, because one play carries several reasons and the rows
// therefore add up to more than the plays they came from. The failure these
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

                plays.Add(APlayReporting(reasons.ToArray()));
            }

            var breakdown = TranscodeReasonBreakdown.Over(plays);

            Assert.Equal((long)length, breakdown.Plays);
            Assert.Equal(
                plays.Count(play => play.Transcode.Reasons.Count > 0),
                breakdown.PlaysWithAtLeastOneReason);
            Assert.Equal(
                breakdown.Reasons.Select(row => row.Reason).Distinct(StringComparer.Ordinal).Count(),
                breakdown.Reasons.Count);

            foreach (var row in breakdown.Reasons)
            {
                Assert.Equal(
                    plays.Count(play => play.Transcode.Reasons.Contains(row.Reason, StringComparer.Ordinal)),
                    row.Plays);
                Assert.InRange(row.Plays, 1, breakdown.PlaysWithAtLeastOneReason);
            }

            for (var i = 1; i < breakdown.Reasons.Count; i++)
            {
                var earlier = breakdown.Reasons[i - 1];
                var later = breakdown.Reasons[i];

                Assert.True(
                    earlier.Plays > later.Plays
                    || (earlier.Plays == later.Plays
                        && string.CompareOrdinal(earlier.Reason, later.Reason) < 0),
                    "Rows are ordered by plays and then by the server's own name, and "
                    + earlier.Reason + " came before " + later.Reason + ".");
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
        Assert.Equal(1, breakdown.Plays);
        Assert.Equal(1, breakdown.PlaysWithAtLeastOneReason);
    }

    /// <summary>
    /// The same plays in a different order are the same answer. A query returns
    /// rows in whatever order its plan produced, and a breakdown whose order
    /// followed that would draw a chart whose bars move when an index changes.
    /// The two reasons here are tied on two plays each, so the tie is settled
    /// by the server's own name rather than by which arrived first.
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
        Assert.Equal(0, breakdown.Plays);
        Assert.Equal(0, breakdown.PlaysWithAtLeastOneReason);
    }

    [Fact]
    public void AMissingSequenceIsRefusedRatherThanReadAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => TranscodeReasonBreakdown.Over(null!));
    }

    private static PlayRecord APlayReporting(params string[] reasons) => new()
    {
        SchemaVersion = 1,
        UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ItemType = "Episode",
        ParentId = Guid.Parse("99999999-8888-7777-6666-555555555555"),
        ItemName = "An episode",
        ItemRuntime = TimeSpan.FromMinutes(42),
        StartedUtc = new DateTime(2026, 3, 14, 9, 0, 0, DateTimeKind.Utc),
        EndedUtc = new DateTime(2026, 3, 14, 9, 41, 0, DateTimeKind.Utc),
        WatchedDuration = TimeSpan.FromMinutes(38),
        ReachedTheEnd = true,
        ClientName = "Jellyfin Web",
        DeviceId = "device-1",
        DeviceName = "A browser",
        PlayMethodAtStart = reasons.Length == 0 ? PlayMethod.DirectPlay : PlayMethod.Transcode,
        PlayMethodChangedUtc = null,
        Transcode = new TranscodeSummary
        {
            VideoCodec = "h264",
            AudioCodec = "aac",
            VideoWasDirect = reasons.Length == 0,
            AudioWasDirect = reasons.Length == 0,
            PeakBitrate = null,
            TypicalBitrate = null,
            HardwareAcceleration = null,
            Reasons = reasons
        }
    };
}
