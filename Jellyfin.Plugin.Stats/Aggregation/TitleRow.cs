using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// One thing that was watched and how much of it was, as a row of a top list.
/// </summary>
/// <remarks>
/// The key is an identifier and never a name, because two items can share a
/// name and a name can change while the thing does not. Grouping on the name
/// would fold two items together on a server that has both, and split one item
/// in two on a server where somebody renamed it halfway through the year.
/// <para>
/// Both figures are carried rather than the one the list was ordered by. A top
/// list by plays and a top list by watched time disagree, and the disagreement
/// is the interesting part, so a reader that wants the other reading has it
/// without a second fold inventing a second definition of either figure.
/// </para>
/// </remarks>
/// <param name="Key">
/// The identifier the plays were grouped under: the item, or the series an
/// episode belongs to.
/// </param>
/// <param name="Name">
/// What to call the row, as the row that named it spelled it, and null where no
/// row carried a name for it. Null rather than a made-up label, for the reason
/// <see cref="DimensionRow"/> gives: an invented word cannot be told from a
/// real one that happens to read the same way. A series is always null today,
/// because a play keeps the name of the item and no name for its parent.
/// </param>
/// <param name="Plays">How many plays fell under this row.</param>
/// <param name="Watched">
/// How much of it was actually watched, which is what the rows recorded as
/// watched rather than the time their sessions were open.
/// </param>
public sealed record TitleRow(Guid Key, string? Name, long Plays, TimeSpan Watched);
