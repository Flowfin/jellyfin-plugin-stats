# This plugin has no custom query surface

No endpoint here accepts SQL, a query fragment, a column name, a table name or
an order-by expression from a caller. Filtering and sorting are chosen from
closed sets defined in the plugin, and a value outside a set is refused rather
than passed through.

This is written down because the convenient thing to do is the opposite, and
because the reason it is refused is not obvious from any one endpoint.

## What the convenient version looks like

The closest prior art has one, and it is restricted to administrators:

    gh api repos/jellyfin/jellyfin-plugin-playbackreporting/contents/Jellyfin.Plugin.PlaybackReporting/Api/PlaybackReportingActivityController.cs \
      --jq '.content' | base64 -d | grep -n 'submit_custom_query\|RequiresElevation'
    36:    [Authorize(Policy = Policies.RequiresElevation)]
    526:        [HttpPost("submit_custom_query")]

Elevation bounds who can use it. It does not bound what it can do. The plugin's
database file is reachable from the server process, so an endpoint that runs a
statement the caller wrote is a general read and write primitive over whatever
that process can open, and an administrator's session token is enough to reach
it. On a plugin whose whole point is that personal detail is visible only to the
user it is about, that one endpoint is the exception that makes the rest of the
design decorative.

It is also the endpoint a recap application asks for a key to use, which is how
the credential ends up somewhere other than the server in the first place.

## What refuses the return of it

`no-query-from-the-request` in `tools/invariants/rules` refuses a request-bound
parameter named for a query, a column, a sort or a grouping.
`no-sql-built-by-concatenation` beside it refuses a statement assembled from
strings. Both fail the run on a match, and both are proved by a near miss of
their own:

    bash tools/invariants/lint.sh --self-test
    ok    no-query-from-the-request fires on its near miss and on no other rule
    ok    no-sql-built-by-concatenation fires on its near miss and on no other rule

The near miss for the first is a sort parameter interpolated rather than mapped,
which is the shape this arrives in when nobody is trying to add a query surface
at all.

## What is not held yet

There are two endpoints now, and the sentence here used to say there are none:

    git grep -lE "ControllerBase|ApiController|HttpGet|HttpPost" -- '*.cs'
    Jellyfin.Plugin.Stats.Tests/AValueTheEndpointCannotReadTests.cs
    Jellyfin.Plugin.Stats.Tests/AuthorizationMatrixTests.cs
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs
    Jellyfin.Plugin.Stats/Api/YourYearController.cs
    tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs
    tools/invariants/near-miss/no-time-offset-from-the-request/DaysInTheCallersZone.cs

The two under `tools/invariants` are near misses and are not compiled. The two
suite files walk the actions by reflection, one asking who each of them admits
and the other what each of them takes.

Between them the two endpoints take four values off a request, and the whole set
is readable in one command:

    git grep -nE 'FromQuery|FromRoute|FromBody|FromForm|FromHeader' -- 'Jellyfin.Plugin.Stats/Api/*.cs'
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:115:        [FromRoute] Guid userId,
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:116:        [FromQuery] DateTimeOffset? from,
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:117:        [FromQuery] DateTimeOffset? to)
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:135:    public async Task<ActionResult<YearInReview>> GetYear([FromRoute] Guid userId, [FromRoute] int year)

An account, a year and two instants. Three of them come off the route and one
pair comes off the query, and not one of them names a column, a table, a
dimension or an order.

So the half of the sentence at the top about a value outside a set being refused
rather than passed through is held for the values that exist. A window instant
the endpoint cannot read is refused rather than being taken for a window nobody
named, which on that route is the difference between an empty request and
somebody's whole history, and a query parameter this plugin declares nothing
about reaches nothing.

**The other half still describes no code.** Filtering and sorting chosen from
closed sets needs a filter or a sort to choose, and there is neither: an instant
is not a member of a set anybody could enumerate, and no endpoint here takes an
order at all. That part remains a statement about how the first report endpoint
is to be written, proved by nothing until one exists. Issue #55 stays open on
it, and the greppable rules above are what stands in the meantime: they refuse
the shape before there is a route to attach it to.
