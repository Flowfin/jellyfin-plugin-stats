// Near miss for no-sql-built-by-concatenation.
//
// The statement above this one is parameterised. This one adds a condition
// during a debugging session and never gets turned back, because the value is
// a Guid the developer knows is a Guid, so it cannot carry anything dangerous.
// The rule is about the shape of the statement rather than about this value.

var rows = connection.Query<PlayRow>(
    $"SELECT ItemId, StartedAt, PlayedTicks FROM plays WHERE UserId = {userId} ORDER BY StartedAt DESC");
