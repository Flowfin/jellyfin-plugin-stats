// What the plugin says about itself, and the one route it has for saying it.
//
// The route is a static seam, because the server's Configuration property is
// not virtual and a plugin therefore cannot put anything of its own into the
// object the settings page is handed. PluginState carries that measurement.
// A static is one thing for the whole process, so the cases below and
// ConfigurationReferenceTests share a collection: that file reads every
// property off a fresh model, and these set what two of them answer.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Capture;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// The plugin's own state, and the settings page's route to it. Issues #31 and
/// #65.
/// </summary>
[Collection(ConfigurationModelCollection.Name)]
public sealed class PluginStateTests
{
    private static readonly DateTime AnOldPlay = new(2026, 4, 1, 20, 15, 0, DateTimeKind.Utc);

    /// <summary>
    /// Before anything reports, both facts are absent. They are absences and
    /// not claims: a plugin nothing has reported on has not been found to have
    /// a broken store, and does not know how far back its rows go.
    /// </summary>
    [Fact]
    public void NothingIsKnownUntilSomethingReports()
    {
        Assert.Null(PluginState.Current.WhyTheStoreCouldNotBeOpened);
        Assert.Null(PluginState.Current.OldestPlayStartedUtc);

        var configuration = new PluginConfiguration();

        Assert.Equal(string.Empty, configuration.WhyTheStoreCouldNotBeOpened);
        Assert.Equal(string.Empty, configuration.OldestStoredPlay);
    }

    /// <summary>
    /// A running report reaches the configuration object, which is the whole of
    /// the route the settings page reads. The instant is written out whole, so
    /// the page can render it in the zone of whoever opened it.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task WhatTheReportSaysIsWhatTheConfigurationObjectCarries()
    {
        var report = new PluginStateReport(() => null, () => AnOldPlay);
        await report.StartAsync(CancellationToken.None);

        try
        {
            var configuration = new PluginConfiguration();

            Assert.Equal(string.Empty, configuration.WhyTheStoreCouldNotBeOpened);
            Assert.Equal("2026-04-01T20:15:00.0000000Z", configuration.OldestStoredPlay);
        }
        finally
        {
            await report.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// The write path's own reason reaches the page. That is the state issue
    /// #31 asks this page to show, and until this route existed it sat on the
    /// writer where nobody outside the process could read it.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AStoreThatCouldNotBeOpenedIsWhatThePageIsGiven()
    {
        var report = new PluginStateReport(
            () => typeof(InvalidOperationException).FullName,
            () => AnOldPlay);

        await report.StartAsync(CancellationToken.None);

        try
        {
            Assert.Equal(
                "System.InvalidOperationException",
                new PluginConfiguration().WhyTheStoreCouldNotBeOpened);
        }
        finally
        {
            await report.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// A read that throws is a store that could not be opened, and is reported
    /// as one rather than as a store holding nothing. The page's sentence turns
    /// on exactly that difference: an empty date under no failure reads as a
    /// server that has never played anything.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AReadThatThrowsIsReportedAsAFailureAndNotAsAnEmptyStore()
    {
        var report = new PluginStateReport(
            () => null,
            () => throw new UnauthorizedAccessException("a message with a path in it"));

        await report.StartAsync(CancellationToken.None);

        try
        {
            var configuration = new PluginConfiguration();

            // The type and never the message. A message out of the store
            // carries the file's path, and this value is on its way to a page.
            Assert.Equal(
                "System.UnauthorizedAccessException",
                configuration.WhyTheStoreCouldNotBeOpened);

            Assert.Equal(string.Empty, configuration.OldestStoredPlay);
        }
        finally
        {
            await report.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Where both opens of the one file failed, the write path's reason is the
    /// one reported. That is the failure costing rows; the reading's own is a
    /// second opinion about the same file.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheWritePathsOwnReasonWinsOverTheReadings()
    {
        var report = new PluginStateReport(
            () => "the writer said so",
            () => throw new UnauthorizedAccessException());

        await report.StartAsync(CancellationToken.None);

        try
        {
            Assert.Equal("the writer said so", new PluginConfiguration().WhyTheStoreCouldNotBeOpened);
        }
        finally
        {
            await report.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Stopping puts the seam back to knowing nothing rather than leaving the
    /// last answer standing. A plugin whose services have stopped is not
    /// holding its store open, so the figure it last read is a claim about a
    /// file nothing is looking at.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task StoppingTakesTheReportBackOut()
    {
        var report = new PluginStateReport(() => "broken", () => AnOldPlay);

        await report.StartAsync(CancellationToken.None);
        await report.StopAsync(CancellationToken.None);

        Assert.Null(PluginState.Current.WhyTheStoreCouldNotBeOpened);
        Assert.Null(PluginState.Current.OldestPlayStartedUtc);

        // Stopping one that never started is not an error either. A host that
        // fails while starting stops everything it built, including services
        // whose start never ran.
        await new PluginStateReport(() => null, () => null).StopAsync(CancellationToken.None);

        Assert.Null(PluginState.Current.WhyTheStoreCouldNotBeOpened);
    }

    /// <summary>
    /// Disposing does what stopping does, because a container disposes a
    /// singleton it built and is not obliged to have called the stop first.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task DisposingTakesTheReportBackOut()
    {
        var report = new PluginStateReport(() => "broken", () => AnOldPlay);

        await report.StartAsync(CancellationToken.None);
        report.Dispose();

        Assert.Null(PluginState.Current.WhyTheStoreCouldNotBeOpened);
    }

    /// <summary>
    /// What cannot be absent is refused where it is taken, rather than at the
    /// first page load that would have used it.
    /// </summary>
    [Fact]
    public void WhatCannotBeAbsentIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => PluginState.ReadFrom(null!));
    }

    /// <summary>
    /// The report is in the container the server builds, beside the listener,
    /// and building it opens nothing.
    /// </summary>
    /// <remarks>
    /// What this does not prove is that the function the registrator hands it
    /// reads the right folder. That function opens the store through the plugin
    /// instance, which is one static for the whole process that other classes
    /// in this suite set while this one runs, and a case resting on it would
    /// fail on whichever run the two overlapped. It is the same bound
    /// <see cref="HeldYearsTests"/> records over the same function.
    /// </remarks>
    [Fact]
    public void TheReportIsResolvedFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISessionManager>(new FakeSessionManager());
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToList();

        Assert.Contains(hosted, service => service is PluginStateReport);
        Assert.Contains(hosted, service => service is PlaybackEventListener);

        // Nothing here has a plugin instance, so a registration that opened the
        // store while resolving would have thrown on the line above.
        Assert.Null(PluginState.Current.WhyTheStoreCouldNotBeOpened);
    }
}

/// <summary>
/// The cases that read the configuration model as a whole, and the ones that
/// set what its two reported fields answer, run one after another.
/// </summary>
/// <remarks>
/// The seam those two fields read is static, so a case holding it while another
/// class read a fresh model off the same type would get a different answer
/// depending on which ran first. This is the smallest measure that removes
/// that: xunit runs one collection at a time, and only two classes are in this
/// one.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class ConfigurationModelCollection
{
    /// <summary>
    /// The name both classes name.
    /// </summary>
    public const string Name = "the configuration model as a whole";
}
