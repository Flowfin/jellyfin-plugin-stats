using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// What the transcoding state of a session came to over one play, kept as a
/// summary rather than as a row per sample.
/// </summary>
/// <remarks>
/// The transcoding state changes while a play runs and is only readable while
/// the session is alive, so it is sampled and folded into this. How it is
/// sampled and folded is issue #37; this type is the shape the row holds, which
/// the row shape had to decide.
/// </remarks>
public sealed record TranscodeSummary
{
    /// <summary>
    /// Gets the video codec the session ended up using, and null where the play
    /// carried no video.
    /// </summary>
    public required string? VideoCodec { get; init; }

    /// <summary>
    /// Gets the audio codec the session ended up using, and null where the play
    /// carried no audio.
    /// </summary>
    public required string? AudioCodec { get; init; }

    /// <summary>
    /// Gets a value indicating whether the video stream was passed through for
    /// the whole play. A play that starts direct and falls back is false here
    /// and carries the reason below.
    /// </summary>
    public required bool VideoWasDirect { get; init; }

    /// <summary>
    /// Gets a value indicating whether the audio stream was passed through for
    /// the whole play.
    /// </summary>
    public required bool AudioWasDirect { get; init; }

    /// <summary>
    /// Gets the highest bitrate observed over the play, in bits per second, and
    /// null where none was reported.
    /// </summary>
    public required int? PeakBitrate { get; init; }

    /// <summary>
    /// Gets the bitrate the play spent most of its samples at, in bits per
    /// second, and null where none was reported.
    /// </summary>
    public required int? TypicalBitrate { get; init; }

    /// <summary>
    /// Gets the hardware acceleration the server reported, and null where it
    /// reported none.
    /// </summary>
    public required string? HardwareAcceleration { get; init; }

    /// <summary>
    /// Gets every transcode reason observed over the play, without repeats.
    /// Reasons are stored as the server reported them and are never worked out
    /// from the codecs afterwards.
    /// </summary>
    public required IReadOnlyList<string> Reasons { get; init; }
}
