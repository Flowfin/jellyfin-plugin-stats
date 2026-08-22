using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.Stats.Configuration;

/// <summary>
/// Connects what the plugin knows about its store to the object the settings
/// page reads, for as long as the host is running.
/// </summary>
/// <remarks>
/// A type of its own rather than a line in the registrator, for the reason
/// <see cref="Capture.PlaybackEventListener"/> is one: the host's start and
/// stop are a lifetime, and something that publishes on start owes a withdrawal
/// on stop. A plugin whose services have stopped must not leave a page reading
/// a figure about a store nothing is holding.
/// <para>
/// Everything it needs arrives as a function, so nothing here opens a store or
/// reaches for the plugin instance, and a test drives it without either.
/// </para>
/// </remarks>
public sealed class PluginStateReport : IHostedService, IDisposable
{
    private readonly Func<string?> _whyTheStoreCouldNotBeOpened;
    private readonly Func<DateTime?> _oldestPlayStartedUtc;

    private IDisposable? _registration;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginStateReport"/> class.
    /// </summary>
    /// <param name="whyTheStoreCouldNotBeOpened">Answers with the write path's own reason, or null where it has none.</param>
    /// <param name="oldestPlayStartedUtc">Reads the oldest stored play, and may throw where the store cannot be read.</param>
    public PluginStateReport(Func<string?> whyTheStoreCouldNotBeOpened, Func<DateTime?> oldestPlayStartedUtc)
    {
        _whyTheStoreCouldNotBeOpened = whyTheStoreCouldNotBeOpened;
        _oldestPlayStartedUtc = oldestPlayStartedUtc;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // A second start replaces the registration rather than being refused,
        // and nothing in the server promises there will not be one. Holding the
        // first instead was written and taken out again: a registration carries
        // no identity, so either spelling leaves the same seam standing and the
        // same withdrawal on stop, and the branch it added was one no case
        // could reach for a reason of its own.
        _registration = PluginState.ReadFrom(Read);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
    }

    /// <summary>
    /// Asks the plugin what it knows, at the moment the page asks.
    /// </summary>
    /// <remarks>
    /// The store is read here rather than at start, because a page opened an
    /// hour into a server's life is asking about the store as it is now. It is
    /// also why a failure is caught: the read is on the path of somebody
    /// loading a settings page, and a plugin that threw out of it would take
    /// the page down instead of telling them what is wrong with the store.
    /// <para>
    /// What is reported is the exception's type and never its message, which is
    /// the shape the write path already uses. A message from the store carries
    /// the file's path, and this value is on its way to a page.
    /// </para>
    /// </remarks>
    /// <returns>What the plugin reports about itself.</returns>
    private PluginState Read()
    {
        var why = _whyTheStoreCouldNotBeOpened();

        try
        {
            return new PluginState(why, _oldestPlayStartedUtc());
        }
        catch (Exception ex)
        {
            // The write path's own reason wins where it has one. That failure
            // is the one that costs rows, and this one is a second opinion
            // about the same file.
            return new PluginState(why ?? ex.GetType().FullName!, null);
        }
    }
}
