using System;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Privacy;

/// <summary>
/// What one account has said about being named, read and written on that
/// account's own asking.
/// </summary>
/// <remarks>
/// Consent governs one thing here: whether an administrator may see somebody's
/// plays as theirs. It never governs whether the rows are kept, which is what
/// the wording in <see cref="ConsentWording"/> tells the person reading it.
/// Issue #42.
/// <para>
/// A type of its own rather than a body inside the endpoint, for the reason
/// <see cref="Api.CallerIdentity"/> is one: an endpoint holding the decision
/// inline could have the identity check deleted from around it and go on
/// answering, and a suite reading status codes would not notice.
/// </para>
/// <para>
/// This names <see cref="IPlayStore"/>, which
/// <c>no-store-write-outside-the-write-path</c> in <c>tools/invariants/rules</c>
/// otherwise refuses, and it is spared there by name. What that rule protects is
/// the capture switch and the per-user exclusion, which sit immediately before a
/// play is written. Nothing here writes a play: the one row this touches is the
/// account's own answer, and it is the account itself that asked for it.
/// </para>
/// </remarks>
public sealed class ConsentRegister
{
    private readonly Func<IPlayStore> _openStore;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsentRegister"/> class.
    /// </summary>
    /// <param name="openStore">Opens the store. Called once per question, and what it returns is disposed of before the answer.</param>
    /// <param name="clock">Says when an answer was given.</param>
    public ConsentRegister(Func<IPlayStore> openStore, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(openStore);
        ArgumentNullException.ThrowIfNull(clock);

        _openStore = openStore;
        _clock = clock;
    }

    /// <summary>
    /// Reads what one account has said, and null where it has said nothing.
    /// </summary>
    /// <remarks>
    /// Null is an answer of its own. An account that has never been asked has
    /// not refused, and a page that read the two the same could never tell
    /// somebody there is a question waiting for them.
    /// </remarks>
    /// <param name="userId">The account.</param>
    /// <returns>What that account has said, or null.</returns>
    public ConsentRecord? For(Guid userId)
        => ReadFromTheStore.Answering(_openStore, store => store.ConsentFor(userId));

    /// <summary>
    /// Records that an account agrees, to a stated version of the wording.
    /// </summary>
    /// <remarks>
    /// The version is refused unless it is the one this build ships. A person
    /// agrees to the words they were shown, so an agreement naming another
    /// version is either a page that has gone stale behind an upgrade or a
    /// caller that made the number up, and both are answered by asking again
    /// rather than by recording an agreement to text nobody read.
    /// <para>
    /// Agreeing again clears the withdrawal rather than keeping it beside the
    /// new agreement. What the record answers is what the account is saying
    /// now, and a withdrawal older than the agreement standing over it is a
    /// moment nothing would read.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account agreeing.</param>
    /// <param name="wordingVersion">The version of the wording the person was shown.</param>
    /// <returns>The record as it now stands.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The version is not the one this build ships.</exception>
    public ConsentRecord Agree(Guid userId, int wordingVersion)
    {
        if (wordingVersion != ConsentWording.Version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wordingVersion),
                wordingVersion,
                "An agreement names the version of the wording the person was shown, and that is not the version this build ships.");
        }

        var record = new ConsentRecord
        {
            UserId = userId,
            Agreed = true,
            AgreedUtc = _clock.GetUtcNow().UtcDateTime,
            WithdrawnUtc = null,
            WordingVersion = wordingVersion
        };

        Write(record);

        return record;
    }

    /// <summary>
    /// Records that an account withdraws.
    /// </summary>
    /// <remarks>
    /// The agreement being withdrawn is kept beside the withdrawal. An account
    /// that agreed in March and withdrew in July has said two things, and a
    /// record holding only the last of them cannot answer for the months in
    /// between.
    /// <para>
    /// An account that never agreed may still withdraw, and what that records
    /// is a refusal: the question was put and the answer was no. Refusing it
    /// because there was nothing to withdraw would leave somebody who wants to
    /// say no with no way to say it.
    /// </para>
    /// </remarks>
    /// <param name="userId">The account withdrawing.</param>
    /// <returns>The record as it now stands.</returns>
    public ConsentRecord Withdraw(Guid userId)
    {
        var standing = For(userId);

        var record = new ConsentRecord
        {
            UserId = userId,
            Agreed = false,
            AgreedUtc = standing?.AgreedUtc,
            WithdrawnUtc = _clock.GetUtcNow().UtcDateTime,
            WordingVersion = standing?.WordingVersion ?? 0
        };

        Write(record);

        return record;
    }

    private void Write(ConsentRecord record)
        => ReadFromTheStore.Answering(
            _openStore,
            store =>
            {
                store.RecordConsent(record);

                return record;
            });
}
