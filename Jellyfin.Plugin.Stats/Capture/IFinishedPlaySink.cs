using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Where a play goes once it is finished and has become a row.
/// </summary>
/// <remarks>
/// The tracker decides what a play is and this is the only thing it hands the
/// result to, so a tracker test holds the rows itself and nothing has to be
/// stored for the decision to be checked.
/// <para>
/// The method is named and shaped to match the store's own add, so the store
/// satisfies this interface by naming it and writing no new code, and the
/// alternative of deleting this interface and typing the tracker on the store
/// is the same one line. It is deliberately not a second abstraction over
/// storage: it has one method, it returns nothing, and nothing reads through
/// it.
/// </para>
/// </remarks>
public interface IFinishedPlaySink
{
    /// <summary>
    /// Takes one finished play.
    /// </summary>
    /// <param name="play">The row the play came to.</param>
    void Add(PlayRecord play);
}
