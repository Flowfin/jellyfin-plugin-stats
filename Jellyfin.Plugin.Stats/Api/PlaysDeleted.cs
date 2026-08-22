namespace Jellyfin.Plugin.Stats.Api;

/// <summary>
/// What a deletion of somebody's own history answers with.
/// </summary>
/// <remarks>
/// A number rather than an empty body, because a page that has just asked for
/// somebody's history to go has one honest thing to say afterwards and it is
/// how much went. An empty success leaves it saying "done" over a request that
/// may have matched nothing, and a caller who named the wrong window would read
/// that as their rows having been removed.
/// </remarks>
public sealed record PlaysDeleted
{
    /// <summary>
    /// Gets how many rows the call removed, and nought where the account had
    /// none in what it named.
    /// </summary>
    public required int Removed { get; init; }
}
