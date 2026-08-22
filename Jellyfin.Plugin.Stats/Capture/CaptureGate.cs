using System;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Decides whether a play is recorded, and is the only thing that decides it.
/// </summary>
/// <remarks>
/// A setting that turns capture off has to stop the writing rather than the
/// reporting. Put the same check in the report layer and the rows are still on
/// disk: the administrator who turned it off has a display preference wearing
/// the name of a control, and the user who asked not to be recorded is being
/// recorded. Issue #39.
/// <para>
/// It judges a play that is still running by the same three answers as a
/// finished one, because a row on the file is a row on the file whether or not
/// the play it is about has ended. A gate that only judged the stop would write
/// an excluded user's viewing to disk for the length of every play they watched
/// and take it away afterwards, which is not what "not recorded" means.
/// </para>
/// <para>
/// A setting that changes part of the way through a play is answered in the
/// direction that keeps least. Capture turned off, or a user excluded, while
/// something is playing means the finished row is refused and the open row that
/// is already on the file is taken away, so the change reaches the rows that
/// exist because of it rather than only the ones after it.
/// </para>
/// <para>
/// This sits between the tracker and the queue, so a play that is not recorded
/// never enters the queue and never reaches the store. It is also why the
/// configuration is read here and not by the writer: the setting takes effect
/// on the next event, and a decision made when the row is drained would apply
/// tonight's setting to a play from before it was changed.
/// </para>
/// <para>
/// The configuration arrives as a function rather than a value, so every play
/// is judged against what the page holds now. A value read once into a field is
/// the setting the page can no longer change, which is the failure
/// <c>no-configuration-value-in-a-static-field</c> refuses and issue #72 is
/// about.
/// </para>
/// </remarks>
public sealed class CaptureGate : IPlaySink
{
    private readonly IPlaySink _inner;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureGate"/> class.
    /// </summary>
    /// <param name="inner">Where a play that is recorded goes next.</param>
    /// <param name="configuration">Reads the configuration as it stands now.</param>
    public CaptureGate(IPlaySink inner, Func<PluginConfiguration> configuration)
    {
        _inner = inner;
        _configuration = configuration;
    }

    /// <summary>
    /// Says whether one play is recorded.
    /// </summary>
    /// <remarks>
    /// The whole decision, in one function, so there is one place to read and
    /// one place a test drives. A second condition added anywhere else is a
    /// rule that holds for the plays that went through that branch and no
    /// others, and nothing would say which.
    /// <para>
    /// The user identifiers are parsed rather than compared as text.
    /// <see cref="PluginConfiguration.ExcludedUserIds"/> refuses an entry that
    /// is not an identifier, so every entry here parses; what it does not
    /// refuse is the same identifier written in a different form, and two
    /// spellings of one user compared as strings would exclude one of them and
    /// record the other.
    /// </para>
    /// <para>
    /// The item types are compared exactly, and that is the setter's doing as
    /// well. <see cref="PluginConfiguration.ExcludedItemTypes"/> keeps only a
    /// value that parses as one of the server's own item kinds, its case
    /// included, and a row's type is that same enumeration turned back into
    /// text. Comparing them any other way would be guarding against a value
    /// the configuration cannot hold.
    /// </para>
    /// </remarks>
    /// <param name="play">The play, finished or still running.</param>
    /// <param name="configuration">The configuration to judge it against.</param>
    /// <returns>True where the play is recorded.</returns>
    public static bool Records(PlayRecord play, PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(play);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.CaptureEnabled)
        {
            return false;
        }

        if (Array.Exists(configuration.ExcludedUserIds, entry => Guid.Parse(entry) == play.UserId))
        {
            return false;
        }

        return !Array.Exists(
            configuration.ExcludedItemTypes,
            entry => string.Equals(entry, play.ItemType, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A play that is not recorded is dropped here and nowhere else, and
    /// nothing is written about it. A log line naming the play would put the
    /// user who asked not to be recorded into the server's log, which is the
    /// same personal detail in a file that outlives every retention setting.
    /// </remarks>
    /// <param name="play">The row the play came to.</param>
    /// <param name="playKey">The key the play's events were joined on.</param>
    public void Add(PlayRecord play, string playKey)
    {
        if (!Records(play, _configuration()))
        {
            // The open row goes even though the finished one does not. It is
            // there because the settings allowed it when the play started, and
            // leaving it would keep on the file exactly what the setting that
            // has since changed exists to stop being kept.
            _inner.ForgetOpen(playKey);
            return;
        }

        _inner.Add(play, playKey);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Judged by the same function as the stop. A play that may not be recorded
    /// may not be recorded while it is running either, and nothing is written
    /// about the refusal for the reason the stop gives.
    /// </remarks>
    /// <param name="play">The play as it stands.</param>
    public void NoteOpen(OpenPlay play)
    {
        ArgumentNullException.ThrowIfNull(play);

        if (!Records(play.SoFar, _configuration()))
        {
            return;
        }

        _inner.NoteOpen(play);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Passed through without being judged. This takes a row away, and a
    /// removal that a setting could refuse is a removal that leaves something
    /// behind when the setting says not to keep it.
    /// </remarks>
    /// <param name="playKey">The key the play's events were joined on.</param>
    public void ForgetOpen(string playKey) => _inner.ForgetOpen(playKey);
}
