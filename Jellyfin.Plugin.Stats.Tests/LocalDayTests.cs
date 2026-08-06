// Days in a zone, checked where they are not 24 hours long and where midnight
// does not exist.
//
// The zones are named by their IANA identifiers, which .NET resolves on both
// platforms this suite runs on, and every moment is a fixed one in the past.
// A test over a transition that has not happened yet would be a test over a
// rule that can still change.

using System;
using Jellyfin.Plugin.Stats.Aggregation;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public class LocalDayTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>
    /// Half past eleven at night is the case the whole of this exists for: in
    /// summer Berlin is two hours ahead of UTC, so the row is stored on the
    /// following day and belongs to this one.
    /// </summary>
    [Theory]
    // The nights either side of the spring transition, 2024-03-31.
    [InlineData("2024-03-30T21:30:00Z", 2024, 3, 30)]
    [InlineData("2024-03-31T21:30:00Z", 2024, 3, 31)]
    // And of the autumn one, 2024-10-27.
    [InlineData("2024-10-26T21:30:00Z", 2024, 10, 26)]
    [InlineData("2024-10-27T22:30:00Z", 2024, 10, 27)]
    public void APlayAtHalfPastElevenBelongsToTheDayTheViewerWasIn(string instant, int year, int month, int day)
    {
        var read = LocalDay.Of(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture), Berlin);

        Assert.Equal(new DateOnly(year, month, day), read);
    }

    /// <summary>
    /// The same moment is two different days for two viewers, which is the
    /// reason a rollup records the zone that produced it.
    /// </summary>
    [Fact]
    public void OneMomentIsADifferentDayInADifferentZone()
    {
        var instant = DateTimeOffset.Parse("2024-06-15T23:30:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2024, 6, 16), LocalDay.Of(instant, Berlin));
        Assert.Equal(new DateOnly(2024, 6, 15), LocalDay.Of(instant, TimeZoneInfo.FindSystemTimeZoneById("America/New_York")));
    }

    [Fact]
    public void TheDayTheClocksGoForwardIsTwentyThreeHoursLong()
    {
        var day = new DateOnly(2024, 3, 31);

        Assert.Equal(TimeSpan.FromHours(23), LocalDay.EndOf(day, Berlin) - LocalDay.StartOf(day, Berlin));
    }

    [Fact]
    public void TheDayTheClocksGoBackIsTwentyFiveHoursLong()
    {
        var day = new DateOnly(2024, 10, 27);

        Assert.Equal(TimeSpan.FromHours(25), LocalDay.EndOf(day, Berlin) - LocalDay.StartOf(day, Berlin));
    }

    /// <summary>
    /// No moment falls between two days and none falls in both, including
    /// across the two transitions, which is what lets a report add days up
    /// without a rule about which side a boundary row belongs to.
    /// </summary>
    [Theory]
    [InlineData(2024, 3, 30)]
    [InlineData(2024, 3, 31)]
    [InlineData(2024, 10, 26)]
    [InlineData(2024, 10, 27)]
    public void ADayStartsWhereTheDayBeforeItEnds(int year, int month, int day)
    {
        var second = new DateOnly(year, month, day);
        var first = second.AddDays(-1);

        Assert.Equal(LocalDay.EndOf(first, Berlin), LocalDay.StartOf(second, Berlin));
    }

    /// <summary>
    /// Chile moves its clocks at midnight, so on the day summer time starts
    /// there is no 00:00 at all. Converting it as if there were is an error and
    /// not an hour that means something slightly different, which is why the
    /// day begins at the first moment that exists.
    /// </summary>
    [Fact]
    public void ADayWhoseMidnightDoesNotExistStartsAtTheFirstMomentThatDoes()
    {
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        var day = new DateOnly(2019, 9, 8);

        var start = LocalDay.StartOf(day, santiago);

        // 04:00 UTC is the transition itself, which is 01:00 in the zone: the
        // first moment of that day there.
        Assert.Equal(DateTimeOffset.Parse("2019-09-08T04:00:00Z", System.Globalization.CultureInfo.InvariantCulture), start);
        Assert.Equal(day, LocalDay.Of(start, santiago));
    }

}
