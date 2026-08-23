using System;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Reports;

/// <summary>
/// Thrown where the range a caller asked over holds more plays than the query
/// surface will read to answer one request.
/// </summary>
/// <remarks>
/// The alternative is what this layer did before: read the bound and fold what
/// came back. That answer is wrong by whatever it did not read and carries
/// nothing that says so, and a truncated report is indistinguishable from a
/// complete one at every point downstream of here. So the request is refused
/// and the refusal names the bound, which is the one thing a caller can act on.
/// <para>
/// A type of its own rather than a plain invalid operation, because the caller
/// that has to turn this into an answer has to tell it apart from a store that
/// could not be opened. The two want different answers: one says ask for less,
/// the other says there is nothing to be had at all.
/// </para>
/// <para>
/// One constructor and none of the three an exception type usually carries, for
/// the reason the exceptions under <c>Data/</c> give: it is thrown from one
/// place, and a constructor nothing calls is a line the suite cannot speak for.
/// </para>
/// <para>
/// Issue #56, first condition.
/// </para>
/// </remarks>
public sealed class TooManyPlaysToAnswerException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TooManyPlaysToAnswerException"/> class.
    /// </summary>
    /// <param name="mostPlays">The bound the range went past.</param>
    public TooManyPlaysToAnswerException(int mostPlays)
        : base(Describe(mostPlays))
    {
        MostPlays = mostPlays;
    }

    /// <summary>
    /// Gets the bound the range went past.
    /// </summary>
    /// <remarks>
    /// Carried as a number as well as said in the message, so a caller can
    /// report it without parsing the sentence back apart.
    /// </remarks>
    public int MostPlays { get; }

    private static string Describe(int mostPlays)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "That range holds more than the {0} plays this plugin will read to answer one request, so it was not answered. Ask over a shorter range. It is refused rather than shortened, because a report folded from part of a range reads exactly like one folded from the whole of it.",
            mostPlays);
    }
}
