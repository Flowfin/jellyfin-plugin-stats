// What one account has said about being named, driven over a temporary
// directory.
//
// Consent here governs one thing: whether an administrator may see somebody's
// plays as theirs. It never governs whether the rows are kept, so nothing in
// this file asserts anything about rows, and the wording the person is shown
// says the same in its own words. Issue #42.

using System;
using System.IO;
using Jellyfin.Plugin.Stats.Data;
using Jellyfin.Plugin.Stats.Privacy;
using Jellyfin.Plugin.Stats.Tests.Fakes;
using Xunit;

namespace Jellyfin.Plugin.Stats.Tests;

public sealed class ConsentRegisterTests : IDisposable
{
    private static readonly Guid Alice = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    private static readonly Guid Bob = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    private static readonly DateTimeOffset March = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root;

    public ConsentRegisterTests()
    {
        _root = Path.Join(Path.GetTempPath(), "jellyfin-plugin-stats-tests", Guid.NewGuid().ToString("N"));
    }

    /// <summary>
    /// An account that has never been asked has said nothing, which is a
    /// different answer from having refused. A page that read the two the same
    /// could never tell somebody there is a question waiting for them.
    /// </summary>
    [Fact]
    public void AnAccountThatWasNeverAskedHasSaidNothing()
        => Assert.Null(ARegister().For(Alice));

    /// <summary>
    /// An agreement is recorded with the moment it was given and the version of
    /// the wording the person was shown, and it comes back off a store opened
    /// again over the same file.
    /// </summary>
    [Fact]
    public void AnAgreementIsRecordedWithItsMomentAndItsVersion()
    {
        ARegister().Agree(Alice, ConsentWording.Version);

        var recorded = ARegister().For(Alice);

        Assert.NotNull(recorded);
        Assert.True(recorded!.Agreed);
        Assert.Equal(March.UtcDateTime, recorded.AgreedUtc);
        Assert.Null(recorded.WithdrawnUtc);
        Assert.Equal(ConsentWording.Version, recorded.WordingVersion);
    }

    /// <summary>
    /// The third condition of issue #42. An agreement naming a version other
    /// than the one this build ships is refused rather than recorded. A person
    /// agrees to the words they were shown, and a page that has gone stale
    /// behind an upgrade would otherwise leave an agreement standing over text
    /// nobody read.
    /// </summary>
    /// <param name="version">The version the request names.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void AnAgreementToAnotherVersionIsRefusedRatherThanRecorded(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ARegister().Agree(Alice, version));

        Assert.Null(ARegister().For(Alice));
    }

    /// <summary>
    /// A withdrawal keeps the agreement it withdraws beside it. An account that
    /// agreed in March and withdrew in July has said two things, and a record
    /// holding only the last of them cannot answer for the months in between.
    /// </summary>
    [Fact]
    public void AWithdrawalKeepsTheAgreementItWithdraws()
    {
        ARegister().Agree(Alice, ConsentWording.Version);
        ARegister(March.AddMonths(4)).Withdraw(Alice);

        var recorded = ARegister().For(Alice);

        Assert.NotNull(recorded);
        Assert.False(recorded!.Agreed);
        Assert.Equal(March.UtcDateTime, recorded.AgreedUtc);
        Assert.Equal(March.AddMonths(4).UtcDateTime, recorded.WithdrawnUtc);
        Assert.Equal(ConsentWording.Version, recorded.WordingVersion);
    }

    /// <summary>
    /// An account that never agreed may still withdraw, and what that records
    /// is a refusal. Refusing it because there was nothing to withdraw would
    /// leave somebody who wants to say no with no way to say it.
    /// </summary>
    [Fact]
    public void AnAccountThatNeverAgreedMayStillSayNo()
    {
        ARegister().Withdraw(Alice);

        var recorded = ARegister().For(Alice);

        Assert.NotNull(recorded);
        Assert.False(recorded!.Agreed);
        Assert.Null(recorded.AgreedUtc);
        Assert.Equal(March.UtcDateTime, recorded.WithdrawnUtc);
        Assert.Equal(0, recorded.WordingVersion);
    }

    /// <summary>
    /// Agreeing again clears the withdrawal rather than leaving it beside the
    /// new agreement. What the record answers is what the account is saying
    /// now, and a withdrawal older than the agreement standing over it is a
    /// moment nothing would read.
    /// </summary>
    [Fact]
    public void AgreeingAgainClearsTheWithdrawal()
    {
        ARegister().Agree(Alice, ConsentWording.Version);
        ARegister(March.AddMonths(4)).Withdraw(Alice);
        ARegister(March.AddMonths(8)).Agree(Alice, ConsentWording.Version);

        var recorded = ARegister().For(Alice);

        Assert.True(recorded!.Agreed);
        Assert.Equal(March.AddMonths(8).UtcDateTime, recorded.AgreedUtc);
        Assert.Null(recorded.WithdrawnUtc);
    }

    /// <summary>
    /// One account's answer is that account's. A register keyed on anything
    /// wider would let one person's agreement speak for another's.
    /// </summary>
    [Fact]
    public void OneAccountsAnswerIsNotAnothers()
    {
        ARegister().Agree(Alice, ConsentWording.Version);

        Assert.True(ARegister().For(Alice)!.Agreed);
        Assert.Null(ARegister().For(Bob));
    }

    /// <summary>
    /// A store that cannot be opened faults with the type an endpoint answers
    /// with a status for, rather than reporting that nobody has agreed. Nothing
    /// and a refusal are different answers, and the second is the one that
    /// reads as settled.
    /// </summary>
    [Fact]
    public void AStoreThatCannotBeOpenedFaultsRatherThanReportingARefusal()
    {
        var register = new ConsentRegister(
            () => throw new IOException("The store is not there."),
            new FixedClock(March));

        Assert.Throws<StoreCouldNotBeOpenedException>(() => register.For(Alice));
        Assert.Throws<StoreCouldNotBeOpenedException>(() => register.Agree(Alice, ConsentWording.Version));
        Assert.Throws<StoreCouldNotBeOpenedException>(() => register.Withdraw(Alice));
    }

    /// <summary>
    /// The register refuses to be built without the two things it cannot work
    /// without.
    /// </summary>
    [Fact]
    public void TheRegisterRefusesToBeBuiltOnNothing()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsentRegister(null!, new FixedClock(March)));
        Assert.Throws<ArgumentNullException>(() => new ConsentRegister(() => new SqlitePlayStore(_root), null!));
    }

    /// <summary>
    /// The store refuses a record it was not given, and a moment that does not
    /// say it is in UTC.
    /// </summary>
    [Fact]
    public void TheStoreRefusesARecordItCannotWrite()
    {
        using var store = new SqlitePlayStore(_root);

        Assert.Throws<ArgumentNullException>(() => store.RecordConsent(null!));

        var local = new ConsentRecord
        {
            UserId = Alice,
            Agreed = true,
            AgreedUtc = DateTime.SpecifyKind(March.UtcDateTime, DateTimeKind.Local),
            WithdrawnUtc = null,
            WordingVersion = ConsentWording.Version
        };

        Assert.Throws<ArgumentException>(() => store.RecordConsent(local));
        Assert.Throws<ArgumentException>(
            () => store.RecordConsent(local with
            {
                Agreed = false,
                AgreedUtc = null,
                WithdrawnUtc = DateTime.SpecifyKind(March.UtcDateTime, DateTimeKind.Unspecified)
            }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private ConsentRegister ARegister(DateTimeOffset? now = null)
        => new(() => new SqlitePlayStore(_root), new FixedClock(now ?? March));
}
