namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One hour of one weekday, and what was played in it.
/// </summary>
/// <remarks>
/// Four fields and no fifth. This is what a view of the week is drawn from, and
/// a view of the week names nobody, so there is no user, no item and no device
/// here to leave out later. A field added to this type is a field that reaches
/// the drawing, which is why the set is asserted rather than reviewed for.
/// <para>
/// The weekday is a number from nought to six with Monday at nought, which is
/// the order the drawing lays its rows out in. Counting the days here in the
/// order they are drawn means the number crosses to the page unchanged; a cell
/// numbered one way and drawn another needs a translation somewhere, and a
/// translation nobody wrote is a grid that is one row out.
/// </para>
/// <para>
/// Watched time is minutes rather than a duration because the drawing scales a
/// number. A duration reaching a page as text is a number the page has to read
/// back, and a page that reads a figure back is a second place the format can
/// be got wrong.
/// </para>
/// <para>
/// Both figures are absent where the hour was never covered, and nought where
/// it was covered and quiet. Those are different facts and a nought said for
/// both is the one this type used to tell: an hour nobody watched anything in,
/// an hour the range never reached and an hour whose rows the retention window
/// deleted all arrived at the drawing as the same figure, and no reading of the
/// picture separated them. The drawing is written to tell them apart and draws
/// an absent cell differently, so what is owed here is the absence itself.
/// Issue #64.
/// </para>
/// </remarks>
public sealed record WeekCell
{
    /// <summary>
    /// Gets the day of the week, from nought for Monday to six for Sunday.
    /// </summary>
    public required int Weekday { get; init; }

    /// <summary>
    /// Gets the hour of that day, from nought to twenty-three, read in the zone
    /// the grid was counted in.
    /// </summary>
    public required int Hour { get; init; }

    /// <summary>
    /// Gets how many plays started in that hour, or null where the range the
    /// figures were read over never reached that hour.
    /// </summary>
    public required long? Plays { get; init; }

    /// <summary>
    /// Gets how many minutes were watched by the plays that started in that
    /// hour, or null where the range never reached that hour.
    /// </summary>
    /// <remarks>
    /// By the plays that started there, and not by what was watched during it.
    /// A play that runs from half past eleven until one in the morning is one
    /// play in one hour, and its whole watched time is counted there. Spreading
    /// it over the hours it covered is a different figure and a harder one: the
    /// row records when a play started and ended and how much of it was
    /// actually watched, and a paused play means the second cannot be laid over
    /// the first without inventing where the pause was.
    /// </remarks>
    public required double? WatchedMinutes { get; init; }
}
