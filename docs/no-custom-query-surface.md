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

## What the endpoints take, and where the choices come from

There are five controllers and ten actions, and the section this replaces
counted four of them:

    git grep -lE "ControllerBase|ApiController|HttpGet|HttpPost" -- '*.cs'
    Jellyfin.Plugin.Stats.Tests/AValueTheEndpointCannotReadTests.cs
    Jellyfin.Plugin.Stats.Tests/AuthorizationMatrixTests.cs
    Jellyfin.Plugin.Stats.Tests/UsageOverTimePageTests.cs
    Jellyfin.Plugin.Stats.Tests/YourStatisticsPageTests.cs
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs
    Jellyfin.Plugin.Stats/Api/YourYearController.cs
    tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs
    tools/invariants/near-miss/no-time-offset-from-the-request/DaysInTheCallersZone.cs

The two under `tools/invariants` are near misses and are not compiled. Of the
four suite files, two walk the actions by reflection, one asking who each of
them admits and the other what each of them takes, and two read a route
attribute out of a controller to hold a page module to the address it asks.
The ten actions themselves:

    git grep -n '\[Http\(Get\|Post\|Put\|Delete\)' -- 'Jellyfin.Plugin.Stats/Api/*.cs'
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:162:    [HttpGet("Top")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:254:    [HttpGet("Breakdown")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:341:    [HttpGet("Usage")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:428:    [HttpGet("Year/{year:int}")]
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:67:    [HttpGet]
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:118:    [HttpPut]
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:108:    [HttpDelete]
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs:128:    [HttpGet("{window}")]
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:161:    [HttpGet]
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:225:    [HttpGet("{year:int}")]

The whole set of values those actions take is readable in one command:

    git grep -nE 'FromQuery|FromRoute|FromBody|FromForm|FromHeader' -- 'Jellyfin.Plugin.Stats/Api/*.cs'
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:169:        [FromQuery] DateTimeOffset? from,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:170:        [FromQuery] DateTimeOffset? to,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:171:        [FromQuery] string? grouping,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:172:        [FromQuery] string? order)
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:261:        [FromQuery] DateTimeOffset? from,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:262:        [FromQuery] DateTimeOffset? to,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:263:        [FromQuery] string? dimension)
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:348:        [FromQuery] DateTimeOffset? from,
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:349:        [FromQuery] DateTimeOffset? to)
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:434:    public async Task<ActionResult<ServerYearInReview>> GetServerYear([FromRoute] int year)
    Jellyfin.Plugin.Stats/Api/ClosedSet.cs:21:/// <c>[FromQuery] TopListOrder? order</c> reads as closed and is not, and what
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:72:    public async Task<ActionResult<ConsentState>> GetConsent([FromRoute] Guid userId)
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:126:        [FromRoute] Guid userId,
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:127:        [FromBody] ConsentAnswer answer)
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:115:        [FromRoute] Guid userId,
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:116:        [FromQuery] DateTimeOffset? from,
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:117:        [FromQuery] DateTimeOffset? to)
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs:135:        [FromRoute] Guid userId,
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs:136:        [FromRoute] string window)
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:166:    public async Task<ActionResult<YearsHeld>> GetYears([FromRoute] Guid userId)
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:231:    public async Task<ActionResult<YearInReview>> GetYear([FromRoute] Guid userId, [FromRoute] int year)

An account, two years, eight window instants, one answer a person gives about
themselves, a grouping, an order, a dimension and a window name. The hit in
`ClosedSet.cs` is a sentence in a remark rather than a parameter, and it is the
shape that file exists to refuse. Not one of the values named is a column, a
table or an expression.

## The grouping, the order, the dimension and the window are the ones this document is about

They are the filters and the sort in this plugin, they arrive as strings, and
none of them reaches anything until it has been compared against a list written
out in the source. `ClosedSet<T>` is that comparison. The spellings are
declared beside the endpoint rather than derived from the enumeration behind
them, so adding a member to that enumeration does not silently widen what a
caller may ask for, and a value in no set is refused before the store is
opened. The grouping and the order were the first two and are the ones the
rest of this section is written about; the dimension a breakdown is cut by and
the window a person's own figures are read over arrived later and go through
the same comparison, and the account is absent from the dimension's
enumeration rather than only from its spellings, so no request can ask for a
breakdown by person whatever it writes.

Refused, not defaulted. A choice named and left blank is not a choice nobody
made, so `?order=` is a 400 rather than whichever member happens to be first.

**The mistake this shape exists against is one word.** An action could declare
`[FromQuery] TopListOrder? order` and read as closed while it is not. Driven at
that shape, a number outside the members is refused by binding and the members'
own numbers are not: `?order=0` and `?order=1` arrive as the two members, and
`?order=` arrives as nothing at all. That is a vocabulary nobody declared and
nobody wrote down, and it changes meaning when a member is reordered.
`NoActionTakesAnEnumerationOffARequest` refuses the shape over every action by
reflection, so the next filter somebody adds inherits the refusal rather than
having to remember it.

**The wire name is `grouping` and not `groupBy`**, and the second is refused by
`no-query-from-the-request` rather than merely disliked: that rule fails the run
on a request-bound parameter whose name is a query, a column, a sort or a
grouping, and it cannot tell a name that maps through a set from one that
reaches a statement. The answer is the name rather than an exception. A rule
argued away once for a good reason is a rule the next parameter is argued away
from for a worse one.

## What is still not held

The elevated route to one person's detail does not exist and is not what this
document is about; that is the authorization matrix. What is left open here is
the bound stated at the top of the greppable rule's own record: neither rule can
judge a NAME. A parameter called `scope` carrying a column list would pass both
of them and would pass the reflection walk as well, because it is a string like
any other. What stands against that is the closed set every choice has to go
through, and a reader of a diff.
