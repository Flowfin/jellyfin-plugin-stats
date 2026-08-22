// Near miss for no-time-offset-from-the-request.
//
// The daily view needs to know where the caller's midnight is, and the page it
// is drawn on already knows: a browser will tell you its offset from UTC in one
// call. So the endpoint grows one more named value beside the two it already
// takes, and it reads like the same move as taking a year or a user.
//
// It is wrong twice a year, in the direction nobody notices. The offset is the
// browser's offset at the moment it asked, and it is applied to rows recorded
// months earlier, so every day either side of a summer transition is shifted by
// an hour and the plays that sat near midnight move to the wrong day. The rows
// do not change, the report does, and it changes again in October.
//
// This is the closest prior art's shape. What replaces it is a zone named once
// in the settings, which is a rule about a place rather than a number about a
// moment, and it is read at the moment a request is served.

[HttpGet("days")]
public ActionResult<IEnumerable<DayRow>> GetDays(
    [FromRoute] Guid userId,
    [FromRoute] int year,
    [FromQuery] int utcOffsetMinutes)
{
    var days = new Dictionary<DateOnly, DayRow>();

    foreach (var play in _plays.For(userId, year))
    {
        var day = DateOnly.FromDateTime(play.StartedUtc.AddMinutes(utcOffsetMinutes));

        days[day] = days.TryGetValue(day, out var row) ? row.Plus(play) : DayRow.Of(play);
    }

    return Ok(days.Values);
}
