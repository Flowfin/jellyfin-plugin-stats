// A breakdown by client and by device, and the two things it must never do.
//
// It must not lose a play. This is a partition, so the rows add up to the plays
// they came from, and the failure it is written against is the one an
// unattributed play causes: a row silently dropped for having no name, and a
// dashboard whose bars total less than the play count beside them.
//
// And it must not name a person. The row type carries no user, so what is
// asserted here is that the type still does not, rather than that this fold
// happened not to fill one in. Every row is built in memory and no clock, zone
// or store is touched.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class DimensionBreakdownTests
{
    private static readonly DateTime Noon = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string[] _clients = { "Jellyfin Web", "Findroid", "Infuse", string.Empty, "   " };

    private static readonly string[] _devices = { "device-1", "device-2", string.Empty };

    /// <summary>
    /// The property both conditions of issue #59 rest on, over sequences nobody
    /// chose one at a time: every play is under exactly one row, so the rows add
    /// up to the plays. It is checked against a count taken the other way round,
    /// walking each row's key and counting the plays that fall to it, so an
    /// accumulator that lost or double counted anything disagrees with it. The
    /// generator is seeded, so a failure is the same failure on the next run and
    /// on the runner.
    /// </summary>
    [Theory]
    [InlineData(PlayDimension.Client)]
    [InlineData(PlayDimension.Device)]
    public void EveryPlayIsUnderExactlyOneRowAndTheOrderIsSettledHere(PlayDimension dimension)
    {
        var generator = new Random(20260813);

        for (var sweep = 0; sweep < 300; sweep++)
        {
            var length = generator.Next(0, 90);
            var plays = new List<PlayRecord>(length);
            for (var i = 0; i < length; i++)
            {
                plays.Add(APlay(
                    client: _clients[generator.Next(_clients.Length)],
                    deviceId: _devices[generator.Next(_devices.Length)],
                    deviceName: generator.Next(3) == 0 ? string.Empty : "A device",
                    method: (PlayMethod)generator.Next(0, 4)));
            }

            var breakdown = DimensionBreakdown.Over(plays, dimension);

            Assert.Equal((long)length, breakdown.Plays);
            Assert.Equal(breakdown.Plays, breakdown.Rows.Sum(row => row.Delivery.Plays));
            Assert.Equal(
                breakdown.Rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count(),
                breakdown.Rows.Count);

            foreach (var row in breakdown.Rows)
            {
                Assert.Equal(
                    plays.Count(play => KeyOf(play, dimension) == row.Key),
                    row.Delivery.Plays);
                Assert.InRange(row.Delivery.Plays, 1, breakdown.Plays);
            }

            for (var i = 1; i < breakdown.Rows.Count; i++)
            {
                var earlier = breakdown.Rows[i - 1];
                var later = breakdown.Rows[i];

                Assert.True(
                    earlier.Delivery.Plays > later.Delivery.Plays
                    || (earlier.Delivery.Plays == later.Delivery.Plays
                        && string.CompareOrdinal(earlier.Key, later.Key) < 0),
                    "Rows are ordered by plays and then by key, and "
                    + earlier.Key + " came before " + later.Key + ".");
            }
        }
    }

    /// <summary>
    /// The third condition of issue #59, as arithmetic rather than as wording. A
    /// play the server named no client for is a row of its own with no name, and
    /// it is still in the total. The failure this is written against is the one
    /// that looks right on a chart: two named bars, a play count of three beside
    /// them, and nothing saying where the third play went.
    /// </summary>
    [Fact]
    public void APlayWithNothingToNameItIsARowAndNotASilence()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(client: "Findroid"),
                APlay(client: "Findroid"),
                APlay(client: string.Empty),
                APlay(client: "   ")
            },
            PlayDimension.Client);

        Assert.Equal(4, breakdown.Plays);
        Assert.Equal(4, breakdown.Rows.Sum(row => row.Delivery.Plays));

        var unnamed = Assert.Single(breakdown.Rows, row => row.Name is null);

        Assert.Equal(string.Empty, unnamed.Key);
        Assert.Equal(2, unnamed.Delivery.Plays);
    }

    /// <summary>
    /// Why the unnamed row carries null and not the word. A client that calls
    /// itself Unknown is a real client with a real name, and a made-up label for
    /// the plays nobody could name would fold the two together into one bar
    /// that is neither.
    /// </summary>
    [Fact]
    public void AClientCalledUnknownIsNotTheRowForPlaysNobodyCouldName()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(client: "Unknown"),
                APlay(client: string.Empty)
            },
            PlayDimension.Client);

        Assert.Equal(2, breakdown.Rows.Count);
        Assert.Contains(breakdown.Rows, row => row.Name == "Unknown" && row.Key == "Unknown");
        Assert.Contains(breakdown.Rows, row => row.Name is null && row.Key.Length == 0);
    }

    /// <summary>
    /// A device is grouped by the identifier the server gave it and labelled
    /// with the name it last reported. Renaming a device on the server would
    /// otherwise split its history into two bars, and the same hardware would
    /// appear twice under a question about which devices transcode.
    /// </summary>
    [Fact]
    public void ARenamedDeviceIsOneRowUnderTheNameItReportedLast()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(deviceId: "device-1", deviceName: "Living room", startedUtc: Noon),
                APlay(deviceId: "device-1", deviceName: "Front room", startedUtc: Noon.AddDays(2)),
                APlay(deviceId: "device-1", deviceName: "Living room", startedUtc: Noon.AddDays(1))
            },
            PlayDimension.Device);

        var row = Assert.Single(breakdown.Rows);

        Assert.Equal("device-1", row.Key);
        Assert.Equal("Front room", row.Name);
        Assert.Equal(3, row.Delivery.Plays);
    }

    /// <summary>
    /// A device whose name the server stopped reporting is unnamed, rather than
    /// keeping a name that is no longer true. It is the same rule as the one
    /// above read the other way, and the two together are what stops the label
    /// being whichever play happened to arrive first.
    /// </summary>
    [Fact]
    public void ADeviceThatStoppedReportingANameLosesIt()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(deviceId: "device-1", deviceName: "Living room", startedUtc: Noon),
                APlay(deviceId: "device-1", deviceName: string.Empty, startedUtc: Noon.AddDays(1))
            },
            PlayDimension.Device);

        var row = Assert.Single(breakdown.Rows);

        Assert.Null(row.Name);
        Assert.Equal(2, row.Delivery.Plays);
    }

    /// <summary>
    /// A device the server gave no identifier for is one row, however many names
    /// those plays reported. There is nothing to tell them apart by, so telling
    /// them apart by name would invent devices that may not exist.
    /// </summary>
    [Fact]
    public void PlaysWithNoDeviceIdentifierAreOneRow()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(deviceId: string.Empty, deviceName: "A browser", startedUtc: Noon),
                APlay(deviceId: string.Empty, deviceName: "Another browser", startedUtc: Noon.AddHours(1))
            },
            PlayDimension.Device);

        var row = Assert.Single(breakdown.Rows);

        Assert.Equal(string.Empty, row.Key);
        Assert.Equal("Another browser", row.Name);
        Assert.Equal(2, row.Delivery.Plays);
    }

    /// <summary>
    /// The transcoded share per client the issue asks for, and it is the same
    /// four figures the whole range answers with rather than a second
    /// definition. A row's figures add up to that row's plays for the same
    /// reason the range's do.
    /// </summary>
    [Fact]
    public void EachRowCarriesTheDeliveryFiguresForItsOwnPlays()
    {
        var breakdown = DimensionBreakdown.Over(
            new[]
            {
                APlay(client: "Findroid", method: PlayMethod.Transcode),
                APlay(client: "Findroid", method: PlayMethod.DirectPlay),
                APlay(client: "Findroid", method: PlayMethod.Unknown),
                APlay(client: "Infuse", method: PlayMethod.DirectStream)
            },
            PlayDimension.Client);

        var findroid = Assert.Single(breakdown.Rows, row => row.Key == "Findroid");

        Assert.Equal(1, findroid.Delivery.Transcode);
        Assert.Equal(1, findroid.Delivery.DirectPlay);
        Assert.Equal(1, findroid.Delivery.Unknown);
        Assert.Equal(0, findroid.Delivery.DirectStream);
        Assert.Equal(3, findroid.Delivery.Plays);
    }

    /// <summary>
    /// The first condition of issue #59, asserted against the answer rather than
    /// against a page. A row that could name a user is a row a page could draw
    /// one from, whatever the page chooses to show, so what is checked is the
    /// shape of the type: these three members and nothing else.
    /// </summary>
    [Fact]
    public void ARowHasNowhereToPutAUser()
    {
        Assert.Equal(
            new[] { "Delivery", "Key", "Name" },
            typeof(DimensionRow)
                .GetProperties()
                .Select(property => property.Name)
                .Where(name => name != "EqualityContract")
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// The dimensions are a closed set and a value outside it is refused. A
    /// caller that asked for something this build has no column for gets an
    /// error rather than a breakdown by whichever dimension came first, because
    /// an answer to the wrong question is worse than none.
    /// </summary>
    [Fact]
    public void ADimensionThisBuildHasNoColumnForIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DimensionBreakdown.Over(new[] { APlay() }, (PlayDimension)99));
    }

    [Fact]
    public void AMissingSequenceIsRefusedRatherThanReadAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => DimensionBreakdown.Over(null!, PlayDimension.Client));
    }

    /// <summary>
    /// The key a play falls under, derived here the way the test reads the
    /// issue rather than the way the fold is written, so the two agree by
    /// measurement and not by sharing a line of code.
    /// </summary>
    private static string KeyOf(PlayRecord play, PlayDimension dimension)
    {
        var value = dimension == PlayDimension.Client ? play.ClientName : play.DeviceId;

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value;
    }

    private static PlayRecord APlay(
        string client = "Jellyfin Web",
        string deviceId = "device-1",
        string deviceName = "A browser",
        PlayMethod method = PlayMethod.DirectPlay,
        DateTime? startedUtc = null) => new()
        {
            SchemaVersion = 1,
            UserId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
            ItemId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            ItemType = "Episode",
            ParentId = null,
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = startedUtc ?? Noon,
            EndedUtc = (startedUtc ?? Noon).AddMinutes(41),
            WatchedDuration = TimeSpan.FromMinutes(38),
            ReachedTheEnd = true,
            ClientName = client,
            DeviceId = deviceId,
            DeviceName = deviceName,
            PlayMethodAtStart = method,
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
