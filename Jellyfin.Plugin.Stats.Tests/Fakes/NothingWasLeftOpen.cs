// The pass over what a restart left open, over a store that holds nothing.
//
// The listener runs that pass before it subscribes, so every test whose subject
// is the subscription needs one and none of them is about it. This is the shape
// that says so: a store with no open rows, read once and disposed of, which
// finishes nothing and writes nothing. A test that IS about the pass builds its
// own store and looks at what came out of it.

using Jellyfin.Plugin.Stats.Capture;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// Builds a <see cref="FinishWhatARestartLeftOpen"/> that finds no open plays.
/// </summary>
public static class NothingWasLeftOpen
{
    /// <summary>
    /// A pass over an empty store.
    /// </summary>
    /// <returns>The pass.</returns>
    public static FinishWhatARestartLeftOpen Pass() => new(() => new HoldablePlayStore());
}
