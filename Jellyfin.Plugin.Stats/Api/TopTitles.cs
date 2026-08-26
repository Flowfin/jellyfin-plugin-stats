using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// What a top list request is answered with.
/// </summary>
/// <remarks>
/// WITHHELD IS NOT EMPTY, and the wire is where that distinction is most easily
/// lost. A range in which nothing was watched and a range whose list may not be
/// shown are different facts, and a response carrying rows alone would answer
/// both with <c>[]</c>. A page drawing that would tell an administrator their
/// server was idle when what happened is that the list stood on too few
/// accounts to be shown. Issue #41 is where the rule is, and the layer says the
/// same thing by answering <c>null</c> rather than an empty list.
/// </remarks>
/// <param name="Withheld">Whether the list was withheld because too few accounts stood behind it.</param>
/// <param name="Rows">The rows, and nothing at all where the list was withheld.</param>
public sealed record TopTitles(bool Withheld, IReadOnlyList<TitleRow> Rows)
{
    /// <summary>
    /// Gets the answer for a list that may not be shown.
    /// </summary>
    public static TopTitles NotShown { get; } = new(true, []);

    /// <summary>
    /// The answer for a list that may be shown.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The answer.</returns>
    public static TopTitles Of(IReadOnlyList<TitleRow> rows) => new(false, rows);
}
