using System;
using System.Collections.Generic;
using System.Globalization;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// The values a request may name for one choice, and the refusal of everything
/// else.
/// </summary>
/// <typeparam name="T">What the choice is, as this plugin spells it internally.</typeparam>
/// <remarks>
/// Issue #55 asks that every filter and sort parameter map through a closed set
/// and that an unknown value be refused rather than passed through. This is the
/// mapping, and it exists as a type rather than as a switch inside an action so
/// that the set can be read, listed back to a caller, and driven by a test with
/// a value nobody declared.
/// <para>
/// THE SHORTCUT THIS TYPE EXISTS AGAINST IS BINDING THE ENUMERATION ITSELF, and
/// it is one word rather than a design. An action declaring
/// <c>[FromQuery] TopListOrder? order</c> reads as closed and is not, and what
/// leaks through it was measured rather than supposed. A number outside the
/// members is refused by binding; what is NOT refused is a member's own number
/// and a blank. <c>?order=0</c> and <c>?order=1</c> reach the action as the two
/// members, and <c>?order=</c> reaches it as no value at all and takes whatever
/// default the action applies. So the endpoint answers to a vocabulary this
/// plugin never declared and never wrote down, chosen by whatever order the
/// members happen to sit in, and renaming or reordering a member silently
/// changes what an old request means.
/// <c>NoActionTakesAnEnumerationOffARequest</c> is what refuses the shape.
/// </para>
/// <para>
/// The spellings are written here rather than derived from the enumeration's
/// member names. A set derived from the type widens itself the day somebody
/// adds a member, which is a decision about what this plugin will answer being
/// taken by an edit that was about something else. Adding a spelling here is
/// the decision, and <c>EverySpellingIsAMemberAndEveryMemberHasOne</c> refuses
/// a set that has drifted from the type in either direction.
/// </para>
/// <para>
/// Matching ignores case and nothing else. A caller writing <c>watchedtime</c>
/// means the member; a caller writing <c>watched time</c>, <c>WatchedTime;--</c>
/// or <c>1</c> does not, and none of those is guessed at.
/// </para>
/// </remarks>
public sealed class ClosedSet<T>
    where T : struct, Enum
{
    private readonly KeyValuePair<string, T>[] _members;
    private readonly string[] _spellings;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClosedSet{T}"/> class.
    /// </summary>
    /// <param name="members">Every spelling a request may carry, and what each one means.</param>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The set is empty, or a spelling appears twice.</exception>
    public ClosedSet(params KeyValuePair<string, T>[] members)
    {
        ArgumentNullException.ThrowIfNull(members);

        if (members.Length == 0)
        {
            throw new ArgumentException(
                "A closed set with nothing in it refuses every request, which reads on the wire exactly like a parameter nobody may use.",
                nameof(members));
        }

        var spellings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (!spellings.Add(member.Key))
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "The spelling {0} appears twice in this set, so which member it means depends on the order the pairs were written in.",
                        member.Key),
                    nameof(members));
            }
        }

        _members = (KeyValuePair<string, T>[])members.Clone();
        _spellings = new string[_members.Length];

        for (var i = 0; i < _members.Length; i++)
        {
            _spellings[i] = _members[i].Key;
        }
    }

    /// <summary>
    /// Gets every spelling this set admits, in the order they were declared.
    /// </summary>
    public IReadOnlyList<string> Spellings => _spellings;

    /// <summary>
    /// Gets what each spelling means.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, T>> Members => _members;

    /// <summary>
    /// Maps what a request named to what it means here.
    /// </summary>
    /// <remarks>
    /// A value that is absent from a request is not this method's business and
    /// is never handed to it as <c>null</c> meaning "the default". An action
    /// that wants a default reads the absence itself, because an empty string
    /// and an absent parameter arrive at an action as different things and a
    /// mapping that treated them alike would answer <c>?order=</c> with
    /// whichever member happened to be first.
    /// </remarks>
    /// <param name="asked">What the request named.</param>
    /// <param name="member">What it means, where it is one of the set.</param>
    /// <returns><c>true</c> where the request named a member of this set.</returns>
    public bool TryMap(string? asked, out T member)
    {
        member = default;

        if (asked is null)
        {
            return false;
        }

        foreach (var candidate in _members)
        {
            if (string.Equals(candidate.Key, asked, StringComparison.OrdinalIgnoreCase))
            {
                member = candidate.Value;
                return true;
            }
        }

        return false;
    }
}
