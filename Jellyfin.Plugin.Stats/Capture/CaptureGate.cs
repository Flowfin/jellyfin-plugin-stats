using System;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Capture;

/// <summary>
/// Decides whether a finished play is recorded, and is the only thing that
/// decides it.
/// </summary>
/// <remarks>
/// A setting that turns capture off has to stop the writing rather than the
/// reporting. Put the same check in the report layer and the rows are still on
/// disk: the administrator who turned it off has a display preference wearing
/// the name of a control, and the user who asked not to be recorded is being
/// recorded. Issue #39.
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
public sealed class CaptureGate : IFinishedPlaySink
{
    private readonly IFinishedPlaySink _inner;
    private readonly Func<PluginConfiguration> _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptureGate"/> class.
    /// </summary>
    /// <param name="inner">Where a play that is recorded goes next.</param>
    /// <param name="configuration">Reads the configuration as it stands now.</param>
    public CaptureGate(IFinishedPlaySink inner, Func<PluginConfiguration> configuration)
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
    /// <param name="play">The finished play.</param>
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
    public void Add(PlayRecord play)
    {
        if (!Records(play, _configuration()))
        {
            return;
        }

        _inner.Add(play);
    }
}
