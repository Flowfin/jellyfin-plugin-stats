using System;

namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// What the consent endpoint answers with.
/// </summary>
/// <remarks>
/// It carries the question as well as the answer. A page that only received the
/// answer would have to fetch the wording separately and could then show one
/// version's words beside another version's number, which is the drift the
/// stored version exists to catch. Issue #42.
/// </remarks>
public sealed record ConsentState
{
    /// <summary>
    /// Gets a value indicating whether the account has been asked and has
    /// answered.
    /// </summary>
    /// <remarks>
    /// Told apart from <see cref="Agreed"/> on purpose. An account that has
    /// never been asked has not refused, and a page that read the two the same
    /// could never tell somebody there is a question waiting for them.
    /// </remarks>
    public required bool Answered { get; init; }

    /// <summary>
    /// Gets a value indicating whether the account is agreeing as things stand.
    /// </summary>
    public required bool Agreed { get; init; }

    /// <summary>
    /// Gets when the account last agreed, in UTC, and null where it never has.
    /// </summary>
    public required DateTime? AgreedUtc { get; init; }

    /// <summary>
    /// Gets when the account last withdrew, in UTC, and null where it never
    /// has.
    /// </summary>
    public required DateTime? WithdrawnUtc { get; init; }

    /// <summary>
    /// Gets the version of the wording the account was shown when it last
    /// agreed, and zero where it has never agreed.
    /// </summary>
    public required int AgreedToVersion { get; init; }

    /// <summary>
    /// Gets the version of the wording this build ships.
    /// </summary>
    /// <remarks>
    /// Beside the version that was agreed to rather than instead of it. The two
    /// being different is what says an agreement stands over words the person
    /// has not read, and it is the whole of what the stored version is for.
    /// </remarks>
    public required int CurrentVersion { get; init; }

    /// <summary>
    /// Gets the wording this build ships, as the person is to read it.
    /// </summary>
    public required string Wording { get; init; }
}
