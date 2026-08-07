// Near miss for no-name-in-a-log-message.
//
// This is the closest prior art's own start-up block, not an invented one. Each
// line is a value the plugin has just read, written out at information level so
// that somebody debugging a play can see what arrived. Three of them are the
// title of what was watched, the client it was watched on and the device it was
// watched from, next to the user the play belongs to a few lines above.
//
// Nobody writes this meaning to publish what a household watches. It reads as
// tracing, it is the shape every one of its neighbours already has, and the
// file it lands in is copied into bug reports.
//
// One more line from the same method is deliberately not here, the one writing
// out the address the session came from. It is refused by a rule of its own, a
// near miss that fires two rules proves neither, and this file cannot even
// spell the field: the other rule reads comments too. That was measured by
// spelling it and watching the self test refuse this file.

_logger.LogInformation("StartPlaybackTimer : e.ClientName         = {ClientName}", e.ClientName);
_logger.LogInformation("StartPlaybackTimer : e.DeviceName         = {DeviceName}", e.DeviceName);
_logger.LogInformation("StartPlaybackTimer : ItemName             = {ItemName}", item_name);
_logger.LogInformation("StartPlaybackTimer : ItemId               = {ItemId}", item_id);
