using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Session;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Folds the transcoding state of one play into the summary its row carries.
/// </summary>
/// <remarks>
/// The server keeps the transcoding state on the session and rewrites it while
/// the play runs, so it is only readable while the session is alive and it is
/// not one value. This is handed each sample as it arrives and holds a running
/// summary, so a play costs one row whatever its length.
/// <para>
/// A sample the server gave nothing for reports nothing. It is not read as a
/// contradiction of an earlier sample and it never turns an observed value back
/// into a null, because a session that stops transcoding has its transcoding
/// state cleared rather than replaced, and the play still transcoded.
/// </para>
/// </remarks>
internal sealed class TranscodeFold
{
    /// <summary>
    /// How many distinct bitrates the typical is drawn from.
    /// </summary>
    /// <remarks>
    /// The peak is a maximum and needs no table. The typical is the value most
    /// samples were at, which does, and a table that took every distinct value
    /// would grow with the play on a server that renegotiates on every progress
    /// report. Past this many, further distinct values still move the peak and
    /// no longer compete for the typical. What that costs is written where
    /// <see cref="Finish"/> hands the summary over.
    /// </remarks>
    private const int TrackedBitrates = 64;

    /// <summary>
    /// Every reason the server can report, read off the enum rather than
    /// listed here, so a reason added to a later server appears without this
    /// file being edited.
    /// </summary>
    private static readonly TranscodeReason[] EveryReason = Enum.GetValues<TranscodeReason>();

    private readonly List<BitrateSamples> _bitrates = new();
    private readonly List<string> _reasons = new();

    private string? _videoCodec;
    private string? _audioCodec;
    private bool _videoWasDirect = true;
    private bool _audioWasDirect = true;
    private int? _peakBitrate;
    private string? _hardwareAcceleration;

    /// <summary>
    /// Folds one sample of the session's transcoding state into the summary.
    /// </summary>
    /// <param name="transcoding">
    /// What the server reported, and null where it reported no transcode at
    /// that moment.
    /// </param>
    public void Observe(TranscodingInfo? transcoding)
    {
        if (transcoding is null)
        {
            // Nothing is being transcoded at this moment. Both streams reached
            // the client as they were, which is what the two flags already say
            // until something says otherwise.
            return;
        }

        // A play is direct only if it was direct the whole way. One sample that
        // says the stream was re-encoded is enough, which is the case of a play
        // that begins direct and falls back.
        _videoWasDirect &= transcoding.IsVideoDirect;
        _audioWasDirect &= transcoding.IsAudioDirect;

        _videoCodec = Reported(_videoCodec, transcoding.VideoCodec);
        _audioCodec = Reported(_audioCodec, transcoding.AudioCodec);

        if (transcoding.HardwareAccelerationType is { } acceleration && acceleration != HardwareAccelerationType.none)
        {
            _hardwareAcceleration = acceleration.ToString();
        }

        if (transcoding.Bitrate is { } bitrate)
        {
            CountBitrate(bitrate);
        }

        AddReasons(transcoding.TranscodeReasons);
    }

    /// <summary>
    /// Closes the fold and produces the summary the row holds.
    /// </summary>
    /// <returns>The summary.</returns>
    /// <remarks>
    /// Where the play carried no transcode at all this is a summary of nulls
    /// and an empty reason list, with both flags saying the streams were passed
    /// through. That is a measurement rather than an absence: the server was
    /// asked on every sample and reported no transcode on any of them.
    /// <para>
    /// The typical bitrate is the value the most samples were at. Where two
    /// values were sampled equally often the one the play reported first wins,
    /// so the same events replayed produce the same summary. Where the play
    /// reported more than <see cref="TrackedBitrates"/> distinct values, the
    /// typical is drawn from the first that many and the rest are in the peak
    /// only.
    /// </para>
    /// </remarks>
    public TranscodeSummary Finish()
    {
        return new TranscodeSummary
        {
            VideoCodec = _videoCodec,
            AudioCodec = _audioCodec,
            VideoWasDirect = _videoWasDirect,
            AudioWasDirect = _audioWasDirect,
            PeakBitrate = _peakBitrate,
            TypicalBitrate = TypicalBitrate(),
            HardwareAcceleration = _hardwareAcceleration,
            Reasons = _reasons.ToArray()
        };
    }

    /// <summary>
    /// The codec the session is using now, or the last one it named.
    /// </summary>
    /// <remarks>
    /// A sample that names no codec leaves the one already held. The server
    /// writes the whole transcoding state at once, so a sample with an empty
    /// codec is the server having nothing to say about that stream rather than
    /// the stream having stopped being what it was.
    /// </remarks>
    /// <param name="held">What the fold already has.</param>
    /// <param name="reported">What this sample named.</param>
    /// <returns>The codec to hold.</returns>
    private static string? Reported(string? held, string? reported)
    {
        if (string.IsNullOrEmpty(reported))
        {
            return held;
        }

        return reported;
    }

    private void CountBitrate(int bitrate)
    {
        _peakBitrate = Math.Max(_peakBitrate ?? bitrate, bitrate);

        for (var i = 0; i < _bitrates.Count; i++)
        {
            if (_bitrates[i].Bitrate == bitrate)
            {
                _bitrates[i] = new BitrateSamples(bitrate, _bitrates[i].Samples + 1);
                return;
            }
        }

        if (_bitrates.Count < TrackedBitrates)
        {
            _bitrates.Add(new BitrateSamples(bitrate, 1));
        }
    }

    private int? TypicalBitrate()
    {
        int? typical = null;
        var most = 0;

        foreach (var seen in _bitrates)
        {
            // Strictly greater, so a tie is settled by the value the play
            // reported first rather than by the order a table happened to hold.
            if (seen.Samples > most)
            {
                most = seen.Samples;
                typical = seen.Bitrate;
            }
        }

        return typical;
    }

    /// <summary>
    /// Adds the reasons this sample carried to the ones the play has shown.
    /// </summary>
    /// <remarks>
    /// The reasons are the server's own, split out of the flags it set and
    /// never worked out from the codecs afterwards. A reason the play reports
    /// on every sample is in the list once, and the order is the order the play
    /// first showed each one.
    /// </remarks>
    /// <param name="reasons">The flags the sample carried.</param>
    private void AddReasons(TranscodeReason reasons)
    {
        foreach (var reason in EveryReason)
        {
            if (!reasons.HasFlag(reason))
            {
                continue;
            }

            var name = reason.ToString();
            if (!_reasons.Contains(name, StringComparer.Ordinal))
            {
                _reasons.Add(name);
            }
        }
    }

    /// <summary>
    /// One bitrate the play reported and how many samples it was at.
    /// </summary>
    /// <param name="Bitrate">The bitrate, in bits per second.</param>
    /// <param name="Samples">How many samples reported it.</param>
    private readonly record struct BitrateSamples(int Bitrate, int Samples);
}
