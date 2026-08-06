using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Stats.Data;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The row shape is what every report, the wrap-up and the privacy design rest
/// on. A column added by habit is a column that gets filled, and the fields this
/// plan refuses are refused because they are cheap to leave out now and
/// impossible to take back once rows carry them. So the set is written down
/// here: adding a column is a deliberate edit to this test rather than a line in
/// a type nobody reads twice.
/// </summary>
public class PlayRecordTests
{
    [Fact]
    public void ThePlayRowHoldsExactlyTheseColumns()
    {
        var expected = new[]
        {
            "ClientName",
            "DeviceId",
            "DeviceName",
            "EndedUtc",
            "ItemId",
            "ItemName",
            "ItemRuntime",
            "ItemType",
            "ParentId",
            "PlayMethod",
            "ReachedTheEnd",
            "SchemaVersion",
            "StartedUtc",
            "Transcode",
            "UserId",
            "WatchedDuration"
        };

        Assert.Equal(expected, ColumnsOf(typeof(PlayRecord)));
    }

    [Fact]
    public void TheTranscodeSummaryHoldsExactlyTheseColumns()
    {
        var expected = new[]
        {
            "AudioCodec",
            "AudioWasDirect",
            "HardwareAcceleration",
            "PeakBitrate",
            "Reasons",
            "TypicalBitrate",
            "VideoCodec",
            "VideoWasDirect"
        };

        Assert.Equal(expected, ColumnsOf(typeof(TranscodeSummary)));
    }

    [Fact]
    public void ARowKeepsTheItemNameItWasWrittenWith()
    {
        // The point of storing the name is a report over items the library no
        // longer has, so the row is read back without anything resolving the
        // identifier it carries.
        var row = ARow() with { ItemName = "An episode that was deleted afterwards" };

        Assert.Equal("An episode that was deleted afterwards", row.ItemName);
    }

    private static string[] ColumnsOf(Type row)
    {
        return row
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static PlayRecord ARow()
    {
        return new PlayRecord
        {
            SchemaVersion = 1,
            UserId = Guid.Parse("2f1a5a4e-1c0a-4a9f-9f0a-9a1b2c3d4e5f"),
            ItemId = Guid.Parse("6b7c8d9e-0f1a-4b2c-8d3e-4f5a6b7c8d9e"),
            ItemType = "Episode",
            ParentId = Guid.Parse("11112222-3333-4444-5555-666677778888"),
            ItemName = "An episode",
            ItemRuntime = TimeSpan.FromMinutes(42),
            StartedUtc = new DateTime(2026, 1, 2, 20, 0, 0, DateTimeKind.Utc),
            EndedUtc = new DateTime(2026, 1, 2, 20, 45, 0, DateTimeKind.Utc),
            WatchedDuration = TimeSpan.FromMinutes(41),
            ReachedTheEnd = true,
            ClientName = "Jellyfin Web",
            DeviceId = "a-device",
            DeviceName = "A device",
            PlayMethod = PlayMethod.DirectPlay,
            Transcode = new TranscodeSummary
            {
                VideoCodec = "h264",
                AudioCodec = "aac",
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = new List<string>()
            }
        };
    }
}
