using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// Which years one account has plays in, and the day this plugin still keeps
/// rows from.
/// </summary>
/// <remarks>
/// What a wrap-up's year selector may offer. Issue #67 asks it to list only
/// years with data and to say why a year is missing when it is missing because
/// of retention, and those are two different facts: the years that are there,
/// and the edge past which no year could be.
/// <para>
/// The list is what the store holds rather than a run between the first year and
/// this one. A quiet year in the middle of a span is not offered, so nobody opens
/// one to find it empty, and a reader who meets a gap inside the kept window is
/// being told that account recorded nothing that year rather than that the year
/// was swept.
/// </para>
/// <para>
/// The day is here rather than left to the page to work out. A page that
/// subtracted a retention setting from its own clock would be answering with the
/// browser's day and the setting's number, which is two readings of two machines
/// where the rows have one.
/// </para>
/// </remarks>
/// <param name="Held">Each calendar year the account has plays in, oldest first, read in the zone the settings name.</param>
/// <param name="KeptFrom">The first day rows are still kept from, as an ISO date, or null where nothing is removed by age.</param>
public sealed record YearsHeld(IReadOnlyList<int> Held, string? KeptFrom);
