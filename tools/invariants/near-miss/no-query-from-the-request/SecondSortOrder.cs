// Near miss for no-query-from-the-request.
//
// A report grows a second sort order. The endpoint already takes the user and
// the window as named values, so adding one more parameter looks like the same
// move, and this one hands the caller a column name that reaches the store.
// Nothing about the line looks like a query endpoint.

[HttpGet("plays")]
public ActionResult<IEnumerable<PlayRow>> GetPlays(
    [FromQuery] Guid userId,
    [FromQuery] DateTime since,
    [FromQuery] string orderBy)
{
    return Ok(_store.PlaysForUser(userId, since, orderBy));
}
