// The near miss for no-store-write-outside-the-write-path.
//
// A report that takes the store itself, because the shape it wants is one the
// query layer does not offer yet and reaching past it is two lines. Having the
// store, it also writes: the placeholder row here stands for a cached total, a
// backfill, a repair somebody ran once. Every one of those bypasses the capture
// switch and the per-user exclusion, which live in one place immediately before
// the write and are the only thing that decides whether a play is recorded at
// all.
//
// This file is not compiled. It exists so the rule that refuses it can be shown
// to bite.

using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.NearMiss;

public sealed class TopItemsReport
{
    private readonly IPlayStore _store;

    public TopItemsReport(IPlayStore store)
    {
        _store = store;
    }

    public void Remember(PlayRecord total)
    {
        _store.Add(total);
    }
}
