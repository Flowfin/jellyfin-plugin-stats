// The mapping every filter and every sort in this plugin goes through, driven
// on its own rather than only through the endpoint that uses it.
//
// Issue #55's second condition is about what happens to a value nobody
// declared. The endpoint cases prove the refusal arrives at a caller as a
// status; these prove the thing doing the refusing knows only what it was
// given, including the two ways of building one that would quietly stop being
// closed.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Api;
using Jellyfin.Plugin.Stats.Reports;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

/// <summary>
/// What a closed set admits and what it refuses.
/// </summary>
public class ClosedSetTests
{
    /// <summary>
    /// Gets the two sets this plugin declares, each beside the enumeration it
    /// is the wire vocabulary for.
    /// </summary>
    public static TheoryData<string> DeclaredSets => ["groupings", "orders"];

    /// <summary>
    /// A spelling the set was given maps to what it was given for, whatever
    /// case it arrives in.
    /// </summary>
    /// <param name="asked">What a request would name.</param>
    /// <param name="expected">What it means.</param>
    [Theory]
    [InlineData("watchedTime", TopListOrder.WatchedTime)]
    [InlineData("WATCHEDTIME", TopListOrder.WatchedTime)]
    [InlineData("watchedtime", TopListOrder.WatchedTime)]
    [InlineData("plays", TopListOrder.Plays)]
    public void ASpellingTheSetWasGivenMaps(string asked, TopListOrder expected)
    {
        Assert.True(AggregateReportsController.Orders.TryMap(asked, out var mapped));
        Assert.Equal(expected, mapped);
    }

    /// <summary>
    /// Anything else maps to nothing.
    /// </summary>
    /// <remarks>
    /// The member name spelled with a space and the member's own number are the
    /// two that matter. The first is what a person writes; the second is what
    /// the framework accepts when the parameter is declared as the enumeration
    /// itself, measured on that shape, and it is the whole reason this type
    /// exists rather than a bound parameter.
    /// </remarks>
    /// <param name="asked">What a request would name.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("watched time")]
    [InlineData("watchedTime ")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("99")]
    [InlineData("Plays;--")]
    [InlineData("UserId")]
    public void ASpellingTheSetWasNotGivenMapsToNothing(string asked)
    {
        Assert.False(AggregateReportsController.Orders.TryMap(asked, out var mapped));
        Assert.Equal(default, mapped);
    }

    /// <summary>
    /// A value that is absent rather than unknown maps to nothing as well.
    /// </summary>
    /// <remarks>
    /// The set has no opinion about a request that made no choice. Deciding
    /// that here would put the default inside the mapping, where an action
    /// asking whether a caller named something could no longer tell a caller
    /// who did not from a caller whose value was erased on the way in.
    /// </remarks>
    [Fact]
    public void NothingAtAllMapsToNothing()
    {
        Assert.False(AggregateReportsController.Orders.TryMap(null, out var mapped));
        Assert.Equal(default, mapped);
    }

    /// <summary>
    /// Every spelling a declared set admits is a member of the enumeration it
    /// is about, and every member has exactly one spelling.
    /// </summary>
    /// <remarks>
    /// THE SET IS WRITTEN OUT AND NOT DERIVED, so this is what stops the two
    /// drifting apart. In one direction a member added to the enumeration would
    /// otherwise be a choice callers can never make, which reads on the wire as
    /// a refusal of a value this build does know. In the other a spelling could
    /// outlive the member it named. Neither is caught by anything else here,
    /// because both compile.
    /// </remarks>
    /// <param name="which">Which of the declared sets.</param>
    [Theory]
    [MemberData(nameof(DeclaredSets))]
    public void EverySpellingIsAMemberAndEveryMemberHasOne(string which)
    {
        var (spellings, meanings, members) = which switch
        {
            "groupings" => (
                AggregateReportsController.Groupings.Spellings,
                AggregateReportsController.Groupings.Members.Select(pair => (int)pair.Value).ToList(),
                Enum.GetValues<TopListGrouping>().Select(member => (int)member).ToList()),
            "orders" => (
                AggregateReportsController.Orders.Spellings,
                AggregateReportsController.Orders.Members.Select(pair => (int)pair.Value).ToList(),
                Enum.GetValues<TopListOrder>().Select(member => (int)member).ToList()),
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "That is not one of the declared sets.")
        };

        Assert.Equal(members.Count, spellings.Count);
        Assert.Equal(members.OrderBy(member => member), meanings.OrderBy(meaning => meaning));
        Assert.Equal(spellings.Count, spellings.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// A set with nothing in it is refused where it is built.
    /// </summary>
    /// <remarks>
    /// It would compile, it would refuse every request, and on the wire that
    /// reads exactly like a parameter nobody is allowed to use. Refusing it at
    /// construction is the difference between a defect that fails a build and
    /// one that answers 400 to everybody until somebody reports it.
    /// </remarks>
    [Fact]
    public void ASetWithNothingInItIsRefused()
        => Assert.Throws<ArgumentException>(() => new ClosedSet<TopListOrder>());

    /// <summary>
    /// A set naming one spelling twice is refused where it is built.
    /// </summary>
    /// <remarks>
    /// Two pairs sharing a spelling make what that spelling means depend on the
    /// order somebody happened to write them in, and the matching ignores case,
    /// so the second case here is the one a reader of the source would not see.
    /// </remarks>
    /// <param name="second">The spelling written the second time.</param>
    [Theory]
    [InlineData("plays")]
    [InlineData("PLAYS")]
    public void ASetNamingOneSpellingTwiceIsRefused(string second)
        => Assert.Throws<ArgumentException>(() => new ClosedSet<TopListOrder>(
            new KeyValuePair<string, TopListOrder>("plays", TopListOrder.Plays),
            new KeyValuePair<string, TopListOrder>(second, TopListOrder.WatchedTime)));

    /// <summary>
    /// A set built out of nothing at all is refused.
    /// </summary>
    [Fact]
    public void ASetBuiltOutOfNothingAtAllIsRefused()
        => Assert.Throws<ArgumentNullException>(() => new ClosedSet<TopListOrder>(null!));
}
