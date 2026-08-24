// A setting changed on the page decides the next request, with no restart
// between.
//
// The cases beside this one hold the other half of issue #72: a setting changed
// between two events decides the second one, and a setting changed between two
// runs of the retention sweep decides the second run. Those are the write path
// and the sweep. This is the read path, which had nothing a caller could reach
// until the query surface landed, and it is the half the issue was left open on.
//
// The endpoints run in this process over the in-memory transport, so nothing
// binds a port and nothing needs a server. The settings object the harness is
// given is the one the endpoints read through, which is what makes changing it
// between two requests the same act as saving the page between them.
//
// Issue #72, first condition.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.Stats.Aggregation;
using Jellyfin.Plugin.Stats.Configuration;
using Jellyfin.Plugin.Stats.Tests.Api;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class ConfigurationTakesEffectOnTheNextQueryTests
{
    /// <summary>
    /// A zone the request is answered in comes from the setting as it stands at
    /// that request.
    /// </summary>
    /// <remarks>
    /// The setting is changed between two requests and nothing else is. A
    /// consumer that read the zone once and held it would answer the second
    /// request in the first request's zone, the page would have saved cleanly,
    /// and nothing anywhere would say the two disagree.
    /// <para>
    /// What the fold was handed is what is asserted, rather than what came back
    /// on the wire. A day boundary is not visible in a year's figures unless the
    /// rows happen to straddle one, so a case reading the response could pass
    /// over an endpoint that had ignored the setting entirely.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheNextRequestIsAnsweredInTheZoneTheSettingHoldsNow()
    {
        var asked = new List<string>();
        var settings = new PluginConfiguration { RollupTimeZone = "Etc/GMT-5" };

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                asked.Add(zone.Id);

                return NothingRecorded(userId, year, zone);
            },
            configuration: settings);

        var who = Caller.Someone;

        Assert.Equal(200, (await endpoints.Get(Year(who), who)).Status);

        // The page saving a new zone, and nothing else. No restart, no new
        // container, no second harness.
        settings.RollupTimeZone = "Etc/GMT+5";

        Assert.Equal(200, (await endpoints.Get(Year(who), who)).Status);

        Assert.Equal(new[] { "Etc/GMT-5", "Etc/GMT+5" }, asked);
    }

    /// <summary>
    /// The answer kept from the earlier request is not handed back under the new
    /// zone.
    /// </summary>
    /// <remarks>
    /// The half a kept answer is most likely to lose. A folded year is held from
    /// the first time it is opened until the rows underneath it move, and a
    /// setting is not a row, so a hold filed under anything less than the zone
    /// would answer the second request out of the first request's fold and the
    /// setting would have changed nothing a reader could see.
    /// <para>
    /// It is asserted by counting the folds rather than by reading the zone
    /// twice, because the case above already reads the zone: two entries there
    /// and one fold here would be a hold that was asked the right question and
    /// answered from the wrong one.
    /// </para>
    /// </remarks>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task TheYearHeldFromTheEarlierRequestIsNotHandedBackUnderTheNewZone()
    {
        var folds = 0;
        var settings = new PluginConfiguration { RollupTimeZone = "Etc/GMT-5" };

        using var endpoints = new InProcessEndpoints(
            fold: (userId, year, zone, topCount) =>
            {
                folds++;

                return NothingRecorded(userId, year, zone);
            },
            configuration: settings);

        var who = Caller.Someone;

        await endpoints.Get(Year(who), who);
        await endpoints.Get(Year(who), who);

        // Two requests under one zone are one fold. That is the hold doing its
        // job, and it is what makes the third request worth anything.
        Assert.Equal(1, folds);

        settings.RollupTimeZone = "Etc/GMT+5";

        await endpoints.Get(Year(who), who);

        Assert.Equal(2, folds);
    }

    /// <summary>
    /// The path of a finished year for one caller.
    /// </summary>
    /// <remarks>
    /// A year that is over rather than the one the fixed clock is in. A year
    /// still running is not held at all, by the rule issue #70 landed, so the
    /// second case below would count two folds under one zone and prove nothing
    /// about the hold.
    /// </remarks>
    /// <param name="who">The caller.</param>
    /// <returns>The path.</returns>
    private static string Year(Caller who)
        => "/Stats/Users/" + who.UserId.ToString("N", System.Globalization.CultureInfo.InvariantCulture) + "/Years/2025";

    /// <summary>
    /// A year with nothing in it, folded over no rows at all.
    /// </summary>
    /// <remarks>
    /// What these cases are about is which zone the fold was asked for, so the
    /// figures are deliberately empty: a year carrying rows would put a day
    /// boundary in the answer and invite a reader to check the wrong thing.
    /// </remarks>
    /// <param name="userId">Whose year.</param>
    /// <param name="year">Which year.</param>
    /// <param name="zone">The zone it was asked for.</param>
    /// <returns>The year.</returns>
    private static YearInReview NothingRecorded(Guid userId, int year, TimeZoneInfo zone)
        => YearInReview.Over([], userId, year, zone, 5, null);
}
