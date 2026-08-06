// Near miss for no-statement-that-drops-a-table.
//
// This is the closest prior art's start-up path, and it is a near miss rather
// than an invented one: the columns found do not match the columns expected, so
// the table goes and comes back empty. Nobody wrote it meaning to delete a
// user's history. It reads as tidying up after a schema that moved, the branch
// is only reached on an upgrade, and it is one line long.
//
// The log line beside it is the whole notice the user gets, after the fact, in
// a file they are not reading.

if (!ColumnsMatch(expected, found))
{
    logger.LogInformation("Schema has changed, recreating the activity table");
    connection.Execute("drop table if exists PlaybackActivity");
    CreateSchema(connection);
}
