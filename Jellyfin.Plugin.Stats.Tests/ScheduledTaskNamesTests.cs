// The names this plugin puts into the scheduled-task namespace, which is
// shared with every other plugin installed on the same server.
//
// The tasks are found the way the server finds them, by walking the plugin
// assembly for the interface, and built the way the server builds them, out of
// this plugin's own registrations. Naming the task types here instead would
// leave a task added later uncompared, which is the gap this file closes: the
// case beside it in UnknownUserSweepTests names two types and passes over a
// third whatever that third is called.
//
// Nothing here opens a store, binds anything or reads a clock. The
// store-opening function is handed on rather than run, so a constructor call is
// all that happens.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class ScheduledTaskNamesTests
{
    /// <summary>
    /// What the server files a task's triggers under, and what it shows the
    /// administrator, for one task this assembly declares.
    /// </summary>
    /// <param name="Type">The type the walk found, named so a failure says which task.</param>
    /// <param name="Key">The identifier the server stores the task's triggers against.</param>
    /// <param name="Name">The line the administrator reads on the scheduled-tasks page.</param>
    private sealed record TaskNames(string Type, string Key, string Name);

    /// <summary>
    /// The walk finds every task this assembly declares, each one builds out of
    /// this plugin's own registrations, and each carries the four strings the
    /// server asks it for.
    /// </summary>
    /// <remarks>
    /// The floor is what stops this file passing over nothing. A walk that
    /// found no type would satisfy every assertion below it by having no
    /// element to break one, and the two cases after this one would report no
    /// collision on an empty set. The number moves only when a task is added or
    /// removed, which is a change somebody makes on purpose.
    /// </remarks>
    [Fact]
    public void EveryScheduledTaskTheAssemblyDeclaresIsBuiltAndNamed()
    {
        var found = EveryTaskTheAssemblyDeclares();

        Assert.True(
            found.Count >= 2,
            $"The walk over the plugin assembly found {found.Count} scheduled task(s), and this plugin declares at least two. A walk that finds nothing reports no collision.");

        foreach (var task in found)
        {
            Assert.False(string.IsNullOrWhiteSpace(task.Key), $"{task.Type} carries no key.");
            Assert.False(string.IsNullOrWhiteSpace(task.Name), $"{task.Type} carries no name.");
        }
    }

    /// <summary>
    /// No two tasks share the key the server files their triggers under.
    /// </summary>
    /// <remarks>
    /// The key is the first name this plugin puts into a namespace it shares
    /// with everything else installed, and a shared one costs one of the two
    /// tasks its triggers. The comparison is over every task the assembly
    /// declares rather than over a pair named here, so a third task added later
    /// is compared without anybody remembering to add it.
    /// <para>
    /// The category is deliberately not compared. Both tasks answer with the
    /// same one, because it is what groups them together on the page, and a
    /// case asserting they differ would refuse the arrangement the plugin
    /// wants.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoTwoScheduledTasksShareAKey()
    {
        var found = EveryTaskTheAssemblyDeclares();

        var shared = found
            .GroupBy(task => task.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(task => task.Type))}")
            .ToList();

        Assert.True(
            shared.Count == 0,
            $"Scheduled tasks in this assembly share a key, and the server files their triggers under it: {string.Join("; ", shared)}");
    }

    /// <summary>
    /// No two tasks show the administrator the same line.
    /// </summary>
    /// <remarks>
    /// Two rows reading alike on the scheduled-tasks page is not a lost
    /// trigger, so it costs less than a shared key and it costs something: the
    /// administrator who stops the one that is running cannot tell which of
    /// them they stopped.
    /// </remarks>
    [Fact]
    public void NoTwoScheduledTasksShareAName()
    {
        var found = EveryTaskTheAssemblyDeclares();

        var shared = found
            .GroupBy(task => task.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(task => task.Type))}")
            .ToList();

        Assert.True(
            shared.Count == 0,
            $"Scheduled tasks in this assembly show the same name on the page: {string.Join("; ", shared)}");
    }

    /// <summary>
    /// Every scheduled task in the plugin assembly, constructed out of a
    /// container holding this plugin's registrations and the one server service
    /// they ask for.
    /// </summary>
    /// <remarks>
    /// The server scans the assembly for the interface and builds what it
    /// finds out of its own container, which is why the walk is a walk and not
    /// a list, and why the tasks are built rather than read off the type. A key
    /// is a property, and a property can be computed.
    /// <para>
    /// The strings are taken while the container is still open and the tasks
    /// are dropped, so nothing here holds a service past the provider that owns
    /// it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TaskNames> EveryTaskTheAssemblyDeclares()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUserManager>(new FakeUserManager());

        new PluginServiceRegistrator().RegisterServices(services, applicationHost: null!);

        using var provider = services.BuildServiceProvider();

        return typeof(Plugin).Assembly
            .GetTypes()
            .Where(type => typeof(IScheduledTask).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type =>
            {
                var task = (IScheduledTask)ActivatorUtilities.CreateInstance(provider, type);

                return new TaskNames(type.Name, task.Key, task.Name);
            })
            .ToList();
    }
}
