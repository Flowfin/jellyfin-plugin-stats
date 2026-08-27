// A store the endpoint suites hand a list of plays to.
//
// The subject in those suites is an endpoint rather than a read, so the rows are
// values and what folds them is the real layer. It lives here rather than inside
// one suite because two of them need it and a second copy is a second set of
// refusals to keep in step.

using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.Stats.Data;

namespace Jellyfin.Plugin.Stats.Tests.Fakes;

/// <summary>
/// A store that answers a range out of a list it was handed.
/// </summary>
/// <remarks>
/// The subject here is the endpoint rather than the read, so the rows are
/// values. What is folded over them is the real layer, and everything this
/// store is not asked is refused rather than answered with nothing.
/// </remarks>
public sealed class PlaysHeldInMemory : IPlayStore
{
    private readonly IReadOnlyList<PlayRecord> _plays;
    private readonly bool _asManyAsAnyBoundAsksFor;

    public PlaysHeldInMemory(IReadOnlyList<PlayRecord> plays, bool asManyAsAnyBoundAsksFor = false)
    {
        _plays = plays;
        _asManyAsAnyBoundAsksFor = asManyAsAnyBoundAsksFor;
    }

    // The bound is asked for as one row past itself, so a store answering
    // with everything it was asked for is a range holding one more play
    // than the layer will read. The same record repeated is enough: what
    // the layer compares is how many came back, and nothing is folded
    // because the refusal is raised first.
    public IReadOnlyList<PlayRecord> PlaysBetween(DateTime fromUtc, DateTime toUtc, int limit)
        => _asManyAsAnyBoundAsksFor
            ? Enumerable.Repeat(_plays[0], limit).ToList()
            : _plays.Where(play => play.StartedUtc >= fromUtc && play.StartedUtc < toUtc).Take(limit).ToList();

    public void Dispose()
    {
    }

    public void Add(PlayRecord play) => throw NotPartOfThis();

    public void NoteOpenPlay(OpenPlay play) => throw NotPartOfThis();

    public void AddAndForgetOpenPlay(PlayRecord play, string playKey) => throw NotPartOfThis();

    public void ForgetOpenPlay(string playKey) => throw NotPartOfThis();

    public IEnumerable<OpenPlay> OpenPlays() => throw NotPartOfThis();

    public ConsentRecord? ConsentFor(Guid userId) => throw NotPartOfThis();

    public void RecordConsent(ConsentRecord consent) => throw NotPartOfThis();

    public void ForgetConsentFor(Guid userId) => throw NotPartOfThis();

    public IReadOnlyList<PlayRecord> MostRecentPlays(int limit) => throw NotPartOfThis();

    public IEnumerable<PlayRecord> AllPlays() => throw NotPartOfThis();

    // A rollup this store never kept. The same refusal as the reads above and
    // for the same reason: answering with none would let a caller that asked
    // about days pass through a fake that has none.
    public TimeZoneInfo? RollupZone => throw NotPartOfThis();

    public IEnumerable<DailyRollup> AllRollups() => throw NotPartOfThis();

    public IEnumerable<PlayRecord> PlaysFor(Guid userId) => throw NotPartOfThis();

    public IReadOnlyList<Guid> UserIdsWithPlays() => throw NotPartOfThis();

    public DateTime? OldestPlayStartedUtc() => throw NotPartOfThis();

    public IReadOnlyList<int> YearsWithPlaysFor(Guid userId, TimeZoneInfo zone) => throw NotPartOfThis();

    public long CountPlaysStartedBefore(DateTime cutoffUtc) => throw NotPartOfThis();

    public int DeletePlaysStartedBefore(DateTime cutoffUtc, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

    public IReadOnlyList<DeletionRecorded> DeletionsRecorded(int limit) => throw NotPartOfThis();

    public int DeletePlaysFor(Guid userId, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

    public int DeletePlaysFor(Guid userId, DateTime fromUtc, DateTime toUtc, DeletionClass deletionClass, int limit) => throw NotPartOfThis();

    public void ReclaimFreedSpace() => throw NotPartOfThis();

    private static NotSupportedException NotPartOfThis()
        => new("This store answers a range and nothing else.");
}
