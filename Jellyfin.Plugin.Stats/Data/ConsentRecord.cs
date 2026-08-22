using System;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// What one account has said about being named in the views this plugin draws.
/// </summary>
/// <remarks>
/// Consent here governs one thing: whether an administrator may see somebody's
/// plays as theirs. It never governs whether the rows are kept, and the wording
/// a person is shown says so. Issue #42.
/// <para>
/// The record carries the version of the wording the person was shown, so a
/// later change to the words does not silently inherit an old agreement. A
/// reader comparing that number against the wording this build ships is what
/// turns an agreement to text nobody has read into a question that gets asked
/// again.
/// </para>
/// <para>
/// A withdrawal keeps the moment of the agreement it withdraws rather than
/// wiping it. The two together are what the record is for: an account that
/// agreed in March and withdrew in July has said two things, and a record
/// holding only the last of them cannot answer for the months in between.
/// </para>
/// </remarks>
public sealed record ConsentRecord
{
    /// <summary>
    /// Gets the account this record is about.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets a value indicating whether the account is agreeing as things stand.
    /// </summary>
    /// <remarks>
    /// The one field every reader has to look at. A record exists for an
    /// account that has withdrawn as well as for one that is agreeing, so its
    /// presence says the question was answered rather than answered yes.
    /// </remarks>
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
    /// <remarks>
    /// Zero rather than a null, because a record that has never held an
    /// agreement has no version to name and a reader comparing numbers should
    /// not have a second case for it. No wording is version zero.
    /// </remarks>
    public required int WordingVersion { get; init; }
}
