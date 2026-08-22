using System;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// What this plugin knows about itself that nobody set: whether its store can
/// be opened, and how far back the rows in it go.
/// </summary>
/// <remarks>
/// These are not settings and they are not stored. They travel to the settings
/// page on the configuration object, beside
/// <see cref="PluginConfiguration.RejectedFields"/>, which is the one value
/// that already reaches that page without being a setting. Issues #31 and #65
/// carry why that route was taken over an endpoint.
/// <para>
/// The seam below is static, which is the cost of that route and is written
/// down here rather than left to be discovered. The server hands the page
/// whatever the plugin's <c>Configuration</c> property returns, and that
/// property is not virtual on either supported server line, so a plugin cannot
/// put anything of its own into the object on the way out. Measured rather than
/// assumed, against the assembly the suite compiles on:
/// </para>
/// <code>
/// typeof(MediaBrowser.Common.Plugins.BasePlugin&lt;&gt;)
///     .GetProperty("Configuration")!.GetGetMethod()!.IsVirtual
/// False
/// </code>
/// <para>
/// So the object has to reach out, and the only thing it can reach is a static.
/// What is kept static is a function that answers, never a value: a value read
/// once into a static is the failure
/// <c>no-configuration-value-in-a-static-field</c> exists against, and this
/// asks the plugin afresh every time the page does.
/// </para>
/// </remarks>
public sealed class PluginState
{
    private static Func<PluginState> _read = NothingIsKnown;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginState"/> class.
    /// </summary>
    /// <param name="whyTheStoreCouldNotBeOpened">Why the store could not be opened, or null where it could.</param>
    /// <param name="oldestPlayStartedUtc">When the oldest stored play started, or null where the store holds none.</param>
    public PluginState(string? whyTheStoreCouldNotBeOpened, DateTime? oldestPlayStartedUtc)
    {
        WhyTheStoreCouldNotBeOpened = whyTheStoreCouldNotBeOpened;
        OldestPlayStartedUtc = oldestPlayStartedUtc;
    }

    /// <summary>
    /// Gets what the plugin currently reports about itself.
    /// </summary>
    /// <remarks>
    /// Where nothing is reporting, this answers with both facts absent rather
    /// than with a claim. A configuration object built by a test, and one on a
    /// server that has not started this plugin's services, are both that case.
    /// </remarks>
    public static PluginState Current => _read();

    /// <summary>
    /// Gets why the store could not be opened, or null where nothing has failed
    /// to open it.
    /// </summary>
    /// <remarks>
    /// One field for both opens of one file. The write path opens the store on
    /// its own thread and records why it could not; the reading behind this
    /// opens it again to ask for the oldest row and can fail the same way.
    /// Reporting those separately would put two sentences about one file in
    /// front of an operator whose next action is the same either way, so a
    /// failure met by either is reported here, and the write path's own reason
    /// is preferred because that is the one that costs rows.
    /// </remarks>
    public string? WhyTheStoreCouldNotBeOpened { get; }

    /// <summary>
    /// Gets when the oldest stored play started, or null where the store holds
    /// no play or could not be read.
    /// </summary>
    /// <remarks>
    /// Null does not on its own mean the store is empty. Where the store could
    /// not be read at all, <see cref="WhyTheStoreCouldNotBeOpened"/> says so and
    /// is what a reader has to look at before calling this an empty store.
    /// </remarks>
    public DateTime? OldestPlayStartedUtc { get; }

    /// <summary>
    /// Says where <see cref="Current"/> is read from, until the returned
    /// registration is disposed.
    /// </summary>
    /// <remarks>
    /// Disposing puts it back to knowing nothing rather than to whatever was
    /// there before. A plugin whose services have stopped knows nothing about
    /// its store, and the previous answer would be a stale claim about a file
    /// nothing is holding open any more.
    /// </remarks>
    /// <param name="read">Answers with the plugin's state.</param>
    /// <returns>A registration that stops the reading when it is disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="read"/> is null.</exception>
    public static IDisposable ReadFrom(Func<PluginState> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        _read = read;

        return new Registration();
    }

    /// <summary>
    /// What is reported while nothing is reporting.
    /// </summary>
    /// <returns>A state with both facts absent.</returns>
    private static PluginState NothingIsKnown() => new(null, null);

    /// <summary>
    /// What <see cref="ReadFrom"/> hands back.
    /// </summary>
    private sealed class Registration : IDisposable
    {
        public void Dispose() => _read = NothingIsKnown;
    }
}
