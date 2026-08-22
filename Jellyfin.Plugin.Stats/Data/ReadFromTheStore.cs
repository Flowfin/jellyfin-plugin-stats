using System;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// One read against a store that is opened for it and closed after it, with the
/// failure to open told apart from everything else.
/// </summary>
/// <remarks>
/// The write path already keeps those two apart, and says why where it does it:
/// a store that cannot be opened is the plugin unable to keep anything, and a
/// row the store refused once it was open is one row. Caught together they are
/// one number and one class of failure. This is the same separation on the
/// reading side, and what it buys is that an endpoint above a read can answer
/// that the plugin is unavailable instead of answering with an empty result.
/// Issue #31 asks for that.
/// <para>
/// Only the open is translated. A read that throws after the file is open is a
/// damaged table or a defect in this plugin, and both come out untouched, so
/// nothing above turns them into a store that is briefly away.
/// </para>
/// <para>
/// It names no store type. The opening function decides what is opened, which
/// is what lets the one place that knows where the data folder is stay the one
/// place that knows, and lets the suite drive this with a store that is a
/// counter. The constraint is the whole of what this needs: something that can
/// be closed again.
/// </para>
/// </remarks>
public static class ReadFromTheStore
{
    /// <summary>
    /// Opens a store, reads it, and closes it again.
    /// </summary>
    /// <typeparam name="TStore">What is opened.</typeparam>
    /// <typeparam name="TAnswer">What the read answers with.</typeparam>
    /// <param name="open">Opens the store, and may fail to.</param>
    /// <param name="read">Reads the open store.</param>
    /// <returns>What the read answered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="open"/> or <paramref name="read"/> is <c>null</c>.</exception>
    /// <exception cref="StoreCouldNotBeOpenedException">The store could not be opened, with what the open threw underneath.</exception>
    public static TAnswer Answering<TStore, TAnswer>(Func<TStore> open, Func<TStore, TAnswer> read)
        where TStore : IDisposable
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(read);

        TStore store;

        try
        {
            store = open();
        }
        catch (Exception ex)
        {
            throw new StoreCouldNotBeOpenedException(ex);
        }

        // Outside the block above rather than inside it, so that a read which
        // throws is not reported as a store that would not open. The store is
        // closed either way, which is what the using is for and is why the open
        // is not simply wrapped whole.
        using (store)
        {
            return read(store);
        }
    }
}
