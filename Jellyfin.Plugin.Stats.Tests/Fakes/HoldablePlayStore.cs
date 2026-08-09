// A store a test can hold still, break, and count. The queue in front of it is
// the thing being measured, and what it is measured by is whether the caller
// gets on with its life while this one refuses to.
//
// Only Add is answered. The three reads throw, because a writer that called one
// of them would be doing something this class is not about, and a fake that
// answered them would let that pass unnoticed.

using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// An <see cref="IPlayStore"/> that can be held open inside a write.
/// </summary>
public sealed class HoldablePlayStore : IPlayStore
{
    private readonly List<PlayRecord> _rows = new();
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _release = new(initialState: true);

    /// <summary>
    /// How long a held write waits before it gives up and lets the suite
    /// finish.
    /// </summary>
    private static readonly TimeSpan NoTestHoldsAWriteThisLong = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Set the moment a write is inside this store, whether or not it is held.
    /// </summary>
    public ManualResetEventSlim Entered { get; } = new(initialState: false);

    /// <summary>
    /// Gets or sets what the next write throws, or null for a write that works.
    /// </summary>
    public Func<Exception>? Throwing { get; set; }

    /// <summary>
    /// Gets whether this store has been disposed of.
    /// </summary>
    public bool Disposed { get; private set; }

    /// <summary>
    /// Gets the rows this store took, in the order it took them.
    /// </summary>
    public IReadOnlyList<PlayRecord> Rows
    {
        get
        {
            lock (_gate)
            {
                return _rows.ToArray();
            }
        }
    }

    /// <summary>
    /// Makes every write from now on block until <see cref="Release"/>.
    /// </summary>
    public void Hold() => _release.Reset();

    /// <summary>
    /// Lets a held write, and every write after it, run.
    /// </summary>
    public void Release() => _release.Set();

    /// <inheritdoc />
    /// <remarks>
    /// The hold has a ceiling on it. A test whose assertion fails before it
    /// reaches its release would otherwise leave this write blocked forever,
    /// and the writer holding it never stops, so the suite hangs instead of
    /// reporting the assertion that failed. A run that reaches the ceiling has
    /// already failed; what the ceiling buys is being told which line.
    /// </remarks>
    public void Add(PlayRecord play)
    {
        Entered.Set();
        _release.Wait(NoTestHoldsAWriteThisLong);

        if (Throwing is not null)
        {
            throw Throwing();
        }

        lock (_gate)
        {
            _rows.Add(play);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

    /// <inheritdoc />
    public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

    /// <inheritdoc />
    public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

    /// <inheritdoc />
    /// <remarks>
    /// The two events are left alive. A test reads them after the writer has
    /// stopped, and disposing of them here would make that read the thing that
    /// fails rather than the thing being asserted.
    /// </remarks>
    public void Dispose() => Disposed = true;

    private static NotSupportedException NotPartOfThis()
        => new("The write path reads nothing, so this fake answers nothing.");
}
