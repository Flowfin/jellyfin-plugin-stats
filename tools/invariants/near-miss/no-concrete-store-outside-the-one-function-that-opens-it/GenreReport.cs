// The near miss for no-concrete-store-outside-the-one-function-that-opens-it.
//
// A report that reaches the plays without going through the query layer, and
// that does it by naming the concrete store rather than the interface. That is
// the shape somebody writes without meaning to break anything: the rule beside
// this one refuses the interface by name, the compiler is happy with either, and
// the class this file names is the one the registrator already opens.
//
// What it costs is the whole of what the query layer is for. The range, the
// bound on how much is read, and the rule that a breakdown standing on one
// account is not answered all live in that layer, and a report holding a store
// re-establishes none of them. The failure is silent: the report works, on the
// author's server, with three accounts on it.
//
// It names no interface, so the rule beside this one does not see it. That is
// the gap this rule closes and the reason there are two rules rather than one
// widened pattern: widening the other one would have spared the registrator from
// the interface rule too, and the registrator is the one file that must stay
// judged by it.
//
// This file is not compiled. It exists so the rule that refuses it can be shown
// to bite.

using System.Collections.Generic;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.NearMiss;

public sealed class GenreReport
{
    private readonly SqlitePlayStore _store;

    public GenreReport(SqlitePlayStore store)
    {
        _store = store;
    }

    public IEnumerable<PlayRecord> Everything() => _store.AllPlays();
}
