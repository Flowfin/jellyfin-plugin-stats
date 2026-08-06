using System.Collections.Generic;

namespace Jellyfin.Plugin.Stats.Data;

/// <summary>
/// One step from the schema version below it to the version it names.
/// </summary>
/// <remarks>
/// A migration is data rather than code, so the list of them is something a test
/// can build for itself and drive the runner over. A runner that could only be
/// exercised through the real list would be a runner nobody could test until
/// there were two real versions to test with, which is one version too late.
/// </remarks>
public sealed record SchemaMigration
{
    /// <summary>
    /// Gets the version the store is at once this has run. Migrations are
    /// applied in ascending order of this.
    /// </summary>
    public required int Version { get; init; }

    /// <summary>
    /// Gets the statements this step runs, in order.
    /// </summary>
    /// <remarks>
    /// They move data and never discard it. The closest prior art answers a
    /// schema change by dropping the table and building it again, which is why
    /// that is refused by an invariant rule rather than left to whoever writes
    /// the next step.
    /// </remarks>
    public required IReadOnlyList<string> Statements { get; init; }
}
