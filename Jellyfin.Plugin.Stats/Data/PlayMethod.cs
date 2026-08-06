namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// How the server delivered an item for one play.
/// </summary>
/// <remarks>
/// This is a closed set of this plugin's own, and not the server's enumeration.
/// A stored row outlives the assembly it was written by, so it must not depend
/// on the numbering of a type that belongs to somebody else's release cycle.
/// </remarks>
public enum PlayMethod
{
    /// <summary>
    /// The value was not reported for this play.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The file was sent as it is on disk.
    /// </summary>
    DirectPlay = 1,

    /// <summary>
    /// The streams were repackaged without being re-encoded. This is the case
    /// the reports call remuxed.
    /// </summary>
    DirectStream = 2,

    /// <summary>
    /// At least one stream was re-encoded.
    /// </summary>
    Transcode = 3
}
