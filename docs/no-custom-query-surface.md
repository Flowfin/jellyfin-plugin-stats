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

There is no endpoint in this plugin. Nothing in the tree carries a controller,
and the one file that matches is the near miss, which is not compiled:

    git grep -lE "ControllerBase|ApiController|HttpGet|HttpPost" -- '*.cs'
    tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs

So the part of this that says every filter and sort parameter maps through a
closed set describes no code. It is a statement about how the first endpoint is
to be written, and it is not proved by anything until one exists. Issue #55
stays open on that, and the greppable rules above are what stands in the
meantime: they refuse the shape before there is a route to attach it to.
