// The three addresses the self statistics page asks at, and the fields it reads
// off the figures answer, held against the C# that serves both. Issue #61.
//
// The page module is JavaScript and the routes it has to agree with are C#, so
// nothing compiles the two together and nothing would say a word if they parted.
// What the module does with an answer is driven by the node suite beside it,
// over fixtures written by hand; this file is about those fixtures being the
// shape the server actually sends, and about the addresses being the right ones.
//
// The failure it exists against is silent in both directions. A field renamed on
// the record reaches a page that refuses every answer and draws the window as
// one that could not be read, and every suite in the tree stays green, because
// the C# side never sees the module and the node side never sees the server. The
// only person who finds out is somebody who opens the page.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Reports;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class YourStatisticsPageTests : IDisposable
{
    private const string Module = "Jellyfin.Plugin.Stats/Pages/yourStatisticsPage.js";

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static readonly Guid Ada = new("11111111111111111111111111111111");

    private readonly string _root;

    public YourStatisticsPageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    /// <summary>
    /// The page asks the address the figures are served at, window segment and
    /// all.
    /// </summary>
    /// <remarks>
    /// The window is part of the address rather than a query, so the route and
    /// the action template are read separately and joined the way the framework
    /// joins them. A page holding only the controller's half would ask for the
    /// collection and be answered by nothing.
    /// </remarks>
    [Fact]
    public void ThePageAsksTheAddressTheFiguresAreServedAt()
    {
        var controller = File.ReadAllText("Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs".Repositioned());
        var route = Match(controller, @"\[Route\(""([^""]+)""\)\]");
        var action = Match(controller, @"\[HttpGet\(""([^""]+)""\)\]");

        Assert.Equal(route + "/" + action, SelfPath("statistics"));
    }

    /// <summary>
    /// The page asks the address consent is read and recorded at.
    /// </summary>
    [Fact]
    public void ThePageAsksTheAddressConsentIsServedAt()
    {
        var controller = File.ReadAllText("Jellyfin.Plugin.Stats/Api/YourConsentController.cs".Repositioned());

        Assert.Equal(Match(controller, @"\[Route\(""([^""]+)""\)\]"), SelfPath("consent"));
    }

    /// <summary>
    /// The page asks the address a person's own plays are deleted at.
    /// </summary>
    [Fact]
    public void ThePageAsksTheAddressPlaysAreDeletedAt()
    {
        var controller = File.ReadAllText("Jellyfin.Plugin.Stats/Api/YourHistoryController.cs".Repositioned());

        Assert.Equal(Match(controller, @"\[Route\(""([^""]+)""\)\]"), SelfPath("plays"));
    }

    /// <summary>
    /// The assembled page is one the server is told about. A page nobody
    /// declares is embedded, unreachable and invisible.
    /// </summary>
    [Fact]
    public void TheSelfStatisticsPageIsDeclaredToTheServer()
    {
        var plugin = (Plugin)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Plugin));

        var page = Assert.Single(plugin.GetPages(), declared => declared.Name == Plugin.YourStatisticsPage);

        Assert.Equal(typeof(Plugin).Namespace + ".Pages.yourStatistics.html", page.EmbeddedResourcePath);
    }

    /// <summary>
    /// Every field the drawing reads off a figures answer is on the answer the
    /// endpoint sends.
    /// </summary>
    /// <remarks>
    /// The names are taken out of the module rather than typed here, so this
    /// case cannot go stale against the file it is about: a field the module
    /// starts reading tomorrow is compared tomorrow, and one it stops reading
    /// stops being required.
    /// <para>
    /// The answer is the real one. A request goes through the framework the
    /// server runs on, to the controller, over a store on disk holding plays,
    /// and what is parsed here is the body that came back, so the casing, the
    /// naming policy and the serializer are the ones a browser would meet.
    /// </para>
    /// <para>
    /// A window with plays in it rather than an empty one, because the series
    /// and the top list are read one element deep and an empty array would
    /// prove nothing about the names inside it.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task EveryFieldTheDrawingReadsIsOnTheAnswerTheEndpointSends()
    {
        Seed(AMayOfPlays(Ada).ToArray());

        using var endpoints = new InProcessEndpoints(
            configuration: new PluginConfiguration { RollupTimeZone = Berlin.Id },
            reports: new AggregateQueries(() => new SqlitePlayStore(_root, Berlin)));

        var who = Caller.Someone;
        var answer = await endpoints.Send("GET", "/Stats/Users/" + who.UserId + "/Statistics/last30Days", who);

        Assert.Equal(200, answer.Status);

        using var body = JsonDocument.Parse(answer.Body);
        var root = body.RootElement;

        var drawing = TheReaderOfAnAnswer();

        var onTheAnswer = FieldsReadOff(drawing, "answer");
        Assert.NotEmpty(onTheAnswer);
        foreach (var field in onTheAnswer)
        {
            Assert.True(
                root.TryGetProperty(field, out _),
                "The drawing reads `" + field + "` off a figures answer and the endpoint sent none.");
        }

        var points = root.GetProperty("points");
        Assert.NotEqual(0, points.GetArrayLength());
        foreach (var field in FieldsReadOff(drawing, "point"))
        {
            Assert.True(
                points[0].TryGetProperty(field, out _),
                "The drawing reads `" + field + "` off a point of the series and the endpoint sent none.");
        }

        var top = root.GetProperty("topItems");
        Assert.NotEqual(0, top.GetArrayLength());
        foreach (var field in FieldsReadOff(drawing, "row"))
        {
            Assert.True(
                top[0].TryGetProperty(field, out _),
                "The drawing reads `" + field + "` off a top row and the endpoint sent none.");
        }
    }

    /// <summary>
    /// The body of the function that turns an answer into what the drawing
    /// takes.
    /// </summary>
    /// <remarks>
    /// Bounded to that one function on purpose. The module holds a second
    /// reader for the consent answer, which reads a different shape off a
    /// different endpoint, and folding the two together would ask this endpoint
    /// for fields it was never meant to carry.
    /// </remarks>
    /// <returns>The source of that function.</returns>
    private static string TheReaderOfAnAnswer()
    {
        var module = File.ReadAllText(Module.Repositioned());
        var from = module.IndexOf("export function forDrawing(answer) {", StringComparison.Ordinal);

        Assert.True(from >= 0, "The module no longer declares forDrawing, so nothing here knows what the page reads.");

        var to = module.IndexOf("\nexport ", from + 1, StringComparison.Ordinal);

        Assert.True(to > from, "forDrawing is the last export in the module, so its end could not be found.");

        return module[from..to];
    }

    /// <summary>
    /// The distinct field names read off one identifier inside a piece of the
    /// module.
    /// </summary>
    /// <param name="source">The source to read.</param>
    /// <param name="identifier">The identifier the fields are read off.</param>
    /// <returns>The names, sorted so a failure names the same one twice running.</returns>
    private static IReadOnlyList<string> FieldsReadOff(string source, string identifier)
        => Regex.Matches(source, @"\b" + Regex.Escape(identifier) + @"\.([A-Za-z][A-Za-z0-9]*)")
            .Select(found => found.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// One of the addresses the module declares.
    /// </summary>
    /// <param name="name">Which one.</param>
    /// <returns>The path.</returns>
    private static string SelfPath(string name)
    {
        var module = File.ReadAllText(Module.Repositioned());

        return Match(module, @"\b" + Regex.Escape(name) + @": '([^']+)',");
    }

    /// <summary>
    /// Reads the one group of the one match, and refuses anything else.
    /// </summary>
    /// <param name="text">What to read.</param>
    /// <param name="pattern">What to look for.</param>
    /// <returns>The captured text.</returns>
    private static string Match(string text, string pattern)
    {
        var found = Regex.Matches(text, pattern);

        Assert.True(found.Count == 1, "The pattern " + pattern + " matched " + found.Count + " times, and a comparison against none or against several proves nothing.");

        return found[0].Groups[1].Value;
    }

    /// <summary>
    /// A stretch of May, which the endpoint's fixed clock of the first of June
    /// leaves inside the last thirty days.
    /// </summary>
    /// <param name="who">Whose plays.</param>
    /// <returns>The plays.</returns>
    private static IEnumerable<PlayRecord> AMayOfPlays(Guid who)
    {
        for (var day = 20; day <= 25; day++)
        {
            for (var play = 0; play < 3; play++)
            {
                yield return APlay(
                    who,
                    new DateTime(2026, 5, day, 18, 0, 0, DateTimeKind.Utc).AddHours(play),
                    TimeSpan.FromMinutes(20 + (play * 15)),
                    reachedTheEnd: play != 2,
                    itemId: new Guid(string.Format(CultureInfo.InvariantCulture, "5555555555555555555555555555555{0}", play)));
            }
        }
    }

    /// <summary>
    /// One finished play.
    /// </summary>
    /// <param name="userId">Whose.</param>
    /// <param name="startedUtc">When it started.</param>
    /// <param name="watched">How much was watched.</param>
    /// <param name="reachedTheEnd">Whether it reached the end.</param>
    /// <param name="itemId">Which item.</param>
    /// <returns>The row.</returns>
    private static PlayRecord APlay(
        Guid userId,
        DateTime startedUtc,
        TimeSpan watched,
        bool reachedTheEnd,
        Guid itemId)
        => new()
        {
            SchemaVersion = SqlitePlayStore.SchemaVersion,
            UserId = userId,
            ItemId = itemId,
            ItemType = "Episode",
            ParentId = new Guid("77777777777777777777777777777777"),
            ItemName = "Something",
            ItemRuntime = TimeSpan.FromMinutes(42),
            ChannelName = null,
            StartedUtc = startedUtc,
            EndedUtc = startedUtc.Add(watched),
            WatchedDuration = watched,
            ReachedTheEnd = reachedTheEnd,
            ClientName = "Jellyfin Web",
            DeviceId = "device-1",
            DeviceName = "A browser",
            PlayMethodAtStart = PlayMethod.DirectPlay,
            PlayMethodChangedUtc = null,
            ClosedBy = PlayClosedBy.AStopEvent,
            Transcode = new TranscodeSummary
            {
                VideoCodec = null,
                AudioCodec = null,
                VideoWasDirect = true,
                AudioWasDirect = true,
                PeakBitrate = null,
                TypicalBitrate = null,
                HardwareAcceleration = null,
                Reasons = Array.Empty<string>(),
            },
        };

    private void Seed(params PlayRecord[] plays)
    {
        Directory.CreateDirectory(_root);

        using var store = new SqlitePlayStore(_root, Berlin);

        foreach (var play in plays)
        {
            store.Add(play);
        }
    }
}
