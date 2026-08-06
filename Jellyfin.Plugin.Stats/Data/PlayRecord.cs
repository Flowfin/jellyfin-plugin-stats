using System;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// One row per play. Every report, the yearly wrap-up and the privacy design
/// rest on this shape, so it is decided in one place and before anything
/// writes.
/// </summary>
/// <remarks>
/// Three things a session carries are deliberately absent: the network endpoint
/// the session came from, the client's user agent string, and the item's
/// location on disk. Nothing in the reports needs any of them, and a column
/// that exists is a column that gets filled, so they are refused here rather
/// than dropped later. An invariant rule refuses them in the source as well.
/// </remarks>
public sealed record PlayRecord
{
    /// <summary>
    /// Gets the version of the row shape this row was written under. It travels
    /// with the row so a reader can tell what it is holding without asking the
    /// store what version it is at.
    /// </summary>
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Gets the user the play belongs to.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets the item that was played.
    /// </summary>
    public required Guid ItemId { get; init; }

    /// <summary>
    /// Gets the item's type, as the server reported it at the time of the play.
    /// </summary>
    public required string ItemType { get; init; }

    /// <summary>
    /// Gets the series or album the item belongs to, where it belongs to one,
    /// and null where it does not.
    /// </summary>
    public required Guid? ParentId { get; init; }

    /// <summary>
    /// Gets the item's name at the time of the play. It is stored rather than
    /// looked up, so a report over items the library no longer has still reads.
    /// </summary>
    public required string ItemName { get; init; }

    /// <summary>
    /// Gets the item's runtime at the time of the play, and null where the item
    /// had none.
    /// </summary>
    public required TimeSpan? ItemRuntime { get; init; }

    /// <summary>
    /// Gets the moment the play started, in UTC.
    /// </summary>
    public required DateTime StartedUtc { get; init; }

    /// <summary>
    /// Gets the moment the play ended, in UTC.
    /// </summary>
    public required DateTime EndedUtc { get; init; }

    /// <summary>
    /// Gets how much of the item was actually watched. This is not the
    /// difference between the two times above: a play that is paused runs on the
    /// clock and not on the item.
    /// </summary>
    public required TimeSpan WatchedDuration { get; init; }

    /// <summary>
    /// Gets a value indicating whether the play reached the end of the item.
    /// </summary>
    public required bool ReachedTheEnd { get; init; }

    /// <summary>
    /// Gets the name of the client application the play came from.
    /// </summary>
    public required string ClientName { get; init; }

    /// <summary>
    /// Gets the identifier of the device the play came from. It identifies a
    /// device and not a person, and it is what a breakdown by device groups on.
    /// </summary>
    public required string DeviceId { get; init; }

    /// <summary>
    /// Gets the device's reported name.
    /// </summary>
    public required string DeviceName { get; init; }

    /// <summary>
    /// Gets how the server delivered the item.
    /// </summary>
    public required PlayMethod PlayMethod { get; init; }

    /// <summary>
    /// Gets what the transcoding state of the session came to over the play.
    /// </summary>
    public required TranscodeSummary Transcode { get; init; }

    /// <summary>
    /// Gets the version of the server that recorded the play, which would be
    /// handy when reading a support request.
    /// </summary>
    public string? ServerVersion { get; init; }
}
