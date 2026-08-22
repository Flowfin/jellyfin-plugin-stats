using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Where a play goes, while it is running and once it is over.
/// </summary>
/// <remarks>
/// The tracker decides what a play is and this is the only thing it hands the
/// result to, so a tracker test holds the rows itself and nothing has to be
/// stored for the decision to be checked.
/// <para>
/// A play reaches this three ways rather than one. <see cref="NoteOpen"/> is
/// what a play that has started and not stopped costs the file, and it is sent
/// again on every progress report, so a play interrupted by a restart is a row
/// somebody can still find. <see cref="Add"/> is the stop, and it carries the
/// key so that the finished row arriving and the open row going are one act.
/// <see cref="ForgetOpen"/> is the case where no finished row is coming and the
/// open one still has to go.
/// </para>
/// <para>
/// It is deliberately not a second abstraction over storage: nothing reads
/// through it, and each of its three methods is one thing the store already
/// does under the same name.
/// </para>
/// </remarks>
public interface IPlaySink
{
    /// <summary>
    /// Takes one finished play, and takes away the open row it came from.
    /// </summary>
    /// <param name="play">The row the play came to.</param>
    /// <param name="playKey">The key the play's events were joined on.</param>
    void Add(PlayRecord play, string playKey);

    /// <summary>
    /// Takes a play that has started and not stopped, replacing whatever was
    /// last taken for the same key.
    /// </summary>
    /// <param name="play">The play as it stands.</param>
    void NoteOpen(OpenPlay play);

    /// <summary>
    /// Takes away the open row for one key, where no finished play is coming.
    /// </summary>
    /// <param name="playKey">The key the play's events were joined on.</param>
    void ForgetOpen(string playKey);
}
