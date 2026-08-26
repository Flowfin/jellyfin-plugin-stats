// What an account may see, without a library to ask. A test that cares about
// the access rule declares the items here; a test about anything else takes the
// one where everything asked about is visible.
//
// The three answers are three separate declarations rather than one nullable
// value, because the case this exists for is a library that changes between the
// write and the read: a test says an item was there, folds a year, then says
// the library has let go of it, and reads the same year again.

using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Aggregation;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// A stand-in for the server's library and its accounts, holding the answers a
/// test declared.
/// </summary>
public sealed class FakeItemAccess : IItemAccess
{
    private readonly HashSet<Guid> _letGoOf = [];
    private readonly HashSet<(Guid UserId, Guid ItemId)> _withheld = [];
    private readonly bool? _forEverythingElse;

    private FakeItemAccess(bool? forEverythingElse)
    {
        _forEverythingElse = forEverythingElse;
    }

    /// <summary>
    /// Gets a library holding every item anybody asks about, visible to
    /// everybody, which is what a test about anything other than access wants.
    /// </summary>
    public static FakeItemAccess EverythingVisible => new(true);

    /// <summary>
    /// Gets a library holding nothing anybody asks about, which is what a
    /// report over items that have all been deleted reads against.
    /// </summary>
    public static FakeItemAccess HoldingNothing => new(null);

    /// <summary>
    /// Gets how many questions were asked.
    /// </summary>
    /// <remarks>
    /// A count rather than a flag, because what a reader wants to know is that
    /// a list of ten costs about ten questions on a year of hundreds of items,
    /// and a flag cannot say that.
    /// </remarks>
    public int TimesAsked { get; private set; }

    /// <summary>
    /// Declares that the library has let go of an item, for everybody.
    /// </summary>
    /// <param name="itemId">The item that is gone.</param>
    /// <returns>This library.</returns>
    public FakeItemAccess LetGoOf(Guid itemId)
    {
        _letGoOf.Add(itemId);
        return this;
    }

    /// <summary>
    /// Declares that the library holds an item and this account may not see it.
    /// </summary>
    /// <param name="userId">The account.</param>
    /// <param name="itemId">The item.</param>
    /// <returns>This library.</returns>
    public FakeItemAccess Withholding(Guid userId, Guid itemId)
    {
        _withheld.Add((userId, itemId));
        return this;
    }

    /// <inheritdoc />
    public bool? MaySee(Guid userId, Guid itemId)
    {
        TimesAsked++;

        if (_letGoOf.Contains(itemId))
        {
            return null;
        }

        if (_withheld.Contains((userId, itemId)))
        {
            return false;
        }

        return _forEverythingElse;
    }
}
