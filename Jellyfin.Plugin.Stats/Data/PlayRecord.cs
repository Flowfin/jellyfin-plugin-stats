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
    /// Gets how the server was delivering the item when the play began.
    /// </summary>
    /// <remarks>
    /// The moment is in the name because this field and
    /// <see cref="Transcode"/> are about different moments and read as one
    /// answer. The delivery method is taken once, off the state the session was
    /// in when playback started; the transcode summary is folded from every
    /// sample that arrived while the play ran. A play that begins direct and is
    /// re-encoded from the second minute has a start method saying direct play
    /// and a summary saying the video was not direct, and both are true.
    /// <para>
    /// It stays the value at the start rather than becoming a fold, because the
    /// transcode shares are computed from it and that was decided on issue #53.
    /// What says the two moments disagree is
    /// <see cref="PlayMethodChangedUtc"/>. Issue #158.
    /// </para>
    /// </remarks>
    public required PlayMethod PlayMethodAtStart { get; init; }

    /// <summary>
    /// Gets when the server first reported a delivery method other than the one
    /// the play began with, and null where it never reported another.
    /// </summary>
    /// <remarks>
    /// The change as its own fact, so a reader can say that the two fields
    /// above are about different moments and when the two parted company. A
    /// row where this is null is a play whose method never moved, and the start
    /// value describes the whole of it.
    /// <para>
    /// The first such moment and not the last. A play can move more than once,
    /// and what a reader of a report needs is whether the start value still
    /// described the play and from when it did not; the last move would answer
    /// neither for a play that moved twice.
    /// </para>
    /// <para>
    /// A sample the server gave no method for leaves this alone, the way a
    /// sample it gave no transcoding state for leaves the summary alone. The
    /// server having nothing to say about a session is not the session having
    /// changed.
    /// </para>
    /// </remarks>
    public required DateTime? PlayMethodChangedUtc { get; init; }

    /// <summary>
    /// Gets what the transcoding state of the session came to over the play.
    /// </summary>
    public required TranscodeSummary Transcode { get; init; }

    /// <summary>
    /// Gets which route ended the play.
    /// </summary>
    /// <remarks>
    /// A play the server sent a stop for and a play something gave up waiting
    /// for were the same row until this, so a report could say how much it had
    /// read and not how much of it ended cleanly. The difference matters to
    /// whoever reads the figures: a row closed on silence has an end that is the
    /// last moment the server heard from the session, and the watching between
    /// that moment and whenever the person actually stopped is not in it.
    /// <para>
    /// <see cref="PlayClosedBy.NotSaid"/> where nothing recorded a route, which
    /// is every row written before this column existed and every row in the
    /// table of plays that are still running. Issue #222.
    /// </para>
    /// </remarks>
    public required PlayClosedBy ClosedBy { get; init; }
}
