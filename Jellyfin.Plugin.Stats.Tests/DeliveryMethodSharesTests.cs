// The four delivery figures, and the one property that holds over all of them.
//
// The property is that what the fold reports adds up to the plays it was given.
// The failure it is written against is a share that quietly drops the plays it
// could not classify, which on a dashboard is indistinguishable from a server
// that delivered fewer plays. Every row here is built in memory and no clock,
// zone or store is touched, so a play an hour long is a test that runs in
// microseconds.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class DeliveryMethodSharesTests
{
    /// <summary>
    /// The property the first condition of issue #53 asks for, over sequences
    /// nobody chose one at a time. The generator is seeded, so a failure is the
    /// same failure on the next run and on the runner; an unseeded one would
    /// report a defect that cannot be reproduced from the output.
    /// <para>
    /// Two of the six values it draws from are outside the set this build
    /// names, so the sweep covers the row a later build wrote as well as the
    /// four this one understands.
    /// </para>
    /// </summary>
    [Fact]
    public void WhatIsReportedAddsUpToThePlaysItWasFoldedFrom()
    {
        var generator = new Random(20260811);
        var methods = new[]
        {
            PlayMethod.Unknown,
            PlayMethod.DirectPlay,
            PlayMethod.DirectStream,
            PlayMethod.Transcode,
            (PlayMethod)7,
            (PlayMethod)(-3)
        };

        for (var sweep = 0; sweep < 500; sweep++)
        {
            var length = generator.Next(0, 200);
            var plays = new List<PlayRecord>(length);
            for (var i = 0; i < length; i++)
            {
                plays.Add(APlayDeliveredBy(methods[generator.Next(methods.Length)]));
            }

            var shares = DeliveryMethodShares.Over(plays);

            Assert.Equal((long)length, shares.Plays);
            Assert.Equal(shares.Plays, shares.Unknown + shares.DirectPlay + shares.DirectStream + shares.Transcode);
        }
    }

    /// <summary>
    /// The decision of 2026-08-09 on issue #53, as an assertion. A play the
    /// server never reported a method for is its own figure, and the direct
    /// ones stay at nought: reporting it as direct would count missing
    /// information as the good outcome.
    /// </summary>
    [Fact]
    public void APlayTheServerNeverReportedAMethodForIsUnknownAndNeverDirect()
    {
        var shares = DeliveryMethodShares.Over(new[] { APlayDeliveredBy(PlayMethod.Unknown) });

        Assert.Equal(1, shares.Unknown);
        Assert.Equal(0, shares.DirectPlay);
        Assert.Equal(0, shares.DirectStream);
        Assert.Equal(0, shares.Transcode);
        Assert.Equal(1, shares.Plays);
    }

    /// <summary>
    /// A row written by a later build can carry a delivery method this one has
    /// no name for. It is counted as unknown rather than dropped, because a
    /// dropped row is a play missing from the answer with nothing saying so,
    /// and that is the one way the property above can be broken.
    /// </summary>
    [Fact]
    public void AMethodThisBuildHasNoNameForIsCountedAsUnknownRatherThanDropped()
    {
        var shares = DeliveryMethodShares.Over(new[] { APlayDeliveredBy((PlayMethod)7) });

        Assert.Equal(1, shares.Unknown);
        Assert.Equal(0, shares.DirectPlay);
        Assert.Equal(1, shares.Plays);
    }

    [Theory]
    [InlineData(PlayMethod.DirectPlay)]
    [InlineData(PlayMethod.DirectStream)]
    [InlineData(PlayMethod.Transcode)]
    public void EachDeliveryTheServerNamesIsCountedUnderItself(PlayMethod method)
    {
        var shares = DeliveryMethodShares.Over(new[] { APlayDeliveredBy(method) });

        Assert.Equal(0, shares.Unknown);
        Assert.Equal(1, method switch
        {
            PlayMethod.DirectPlay => shares.DirectPlay,
            PlayMethod.DirectStream => shares.DirectStream,
            _ => shares.Transcode
        });
        Assert.Equal(1, shares.Plays);
    }

    /// <summary>
    /// A range with no plays in it answers with four zeroes rather than with
    /// nothing, so a caller does not have to tell an empty answer from an
    /// absent one. What it does not do is divide, because nought out of nought
    /// has no percentage and a chart showing nought per cent everywhere would
    /// be stating something the rows do not say.
    /// </summary>
    [Fact]
    public void AnEmptyRangeIsFourZeroesRatherThanNoAnswer()
    {
        var shares = DeliveryMethodShares.Over(Array.Empty<PlayRecord>());

        Assert.Equal(0, shares.Unknown);
        Assert.Equal(0, shares.DirectPlay);
        Assert.Equal(0, shares.DirectStream);
        Assert.Equal(0, shares.Transcode);
        Assert.Equal(0, shares.Plays);
    }

    /// <summary>
    /// The source is the method the row carries and not the transcoding summary
    /// folded over the play, which is the other half of the decision of
    /// 2026-08-09. The row here says the server delivered the file as it is on
    /// disk and its summary says the video was re-encoded and names a reason,
    /// which is the disagreement issue #158 is about. The figure follows the
    /// method, and this test is what would go red if a later change quietly
    /// made it follow the summary instead.
    /// </summary>
    [Fact]
    public void TheFigureFollowsTheMethodTheRowCarriesAndNotItsTranscodeSummary()
    {
        var disagreeing = APlayDeliveredBy(PlayMethod.DirectPlay) with
        {
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = false,
                AudioWasDirect = false,
                PeakBitrate = 8_000_000,
                TypicalBitrate = 6_000_000,
                HardwareAcceleration = null,
                Reasons = new[] { "VideoCodecNotSupported" }
            }
        };

        var shares = DeliveryMethodShares.Over(new[] { disagreeing });

        Assert.Equal(1, shares.DirectPlay);
        Assert.Equal(0, shares.Transcode);
    }

    [Fact]
    public void AMissingSequenceIsRefusedRatherThanReadAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => DeliveryMethodShares.Over(null!));
    }

    private static PlayRecord APlayDeliveredBy(PlayMethod method) => new()
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
        PlayMethod = method,
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
