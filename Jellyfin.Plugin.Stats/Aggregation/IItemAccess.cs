using System;

namespace Jellyfin.Plugin.Stats.Aggregation;

/// <summary>
/// Whether an account may see an item the library holds, asked at the moment a
/// report is read.
/// </summary>
/// <remarks>
/// The one question the library is asked on the read path, and it is one
/// question with one answer: may this account see this item. Not what it is
/// called, not what it belongs to, not what genre it is. Every label a report
/// prints comes off the row that was written when the play happened, so a
/// rename cannot make a report disagree with itself and there is no enrichment
/// path a name could leak through. Issue #54.
/// <para>
/// It is asked now rather than stored on the row because access is a fact about
/// now. A row that had recorded who could see the item would be answering
/// today's question with the answer it had on the day of the play, and a
/// library reorganised since then would leave the report confidently wrong in
/// whichever direction the reorganisation went.
/// </para>
/// <para>
/// Three answers rather than two, and the third is the one this issue turns on.
/// An item the library no longer holds is not an item somebody may not see: it
/// is an item there is no access question about, and the rows recording plays
/// of it are the caller's own. Collapsing that into "not visible" would empty a
/// report of every deleted item, which is the first thing issue #54 asks it not
/// to do.
/// </para>
/// <para>
/// It is a seam rather than a call for the reason <see cref="Capture.IChannelNames"/>
/// gives at the write path: a suite that folds a year has no library to resolve
/// anything through, and a report whose access rule can only be exercised
/// against a running server has a rule nobody can prove bites.
/// </para>
/// </remarks>
public interface IItemAccess
{
    /// <summary>
    /// Whether the account may see the item.
    /// </summary>
    /// <param name="userId">The account the report is being answered for.</param>
    /// <param name="itemId">The item a row of the report would name.</param>
    /// <returns>
    /// True where the library holds the item and this account may see it, false
    /// where it holds it and they may not, and null where the library holds no
    /// such item any more.
    /// </returns>
    bool? MaySee(Guid userId, Guid itemId);
}
