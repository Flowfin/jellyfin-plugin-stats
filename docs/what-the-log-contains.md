# What this plugin writes to the server log

The server log is not the database. It is read by more people, it is pasted into
bug reports and forum posts, and it outlives every retention setting this plugin
has. A line naming who watched what has moved personal detail into a place none
of this plugin's own rules reach, and no later deletion follows it there.

So the rule is short. **Identifiers, not names.**

## What a line may carry

- The play session identifier, or the session identifier where the play is
  already over. These are opaque strings the server made up for one playback and
  they mean nothing once the server has forgotten them.
- Which event was being handled, as a fixed phrase this plugin chose.
- A file system path this plugin itself writes to, in the messages about its own
  data folder and configuration file. An administrator who is told a file could
  not be deleted needs to be told which file.
- The exception, where one was caught.

## What a line may not carry

- A user name, a user's display name, or anything else a person is called.
- An item title, a series name, an album name, or an episode name.
- A consent state, because whether somebody agreed to be counted is itself
  something about that person.
- The body of an export or of a deletion, because the whole point of those is
  that the data goes somewhere the log is not.

## What the plugin logs today

Nothing on the ordinary path. A play that starts, reports progress and stops
writes no line at all, at any level. There is no line saying a play was
recorded, because on a server a household shares, one line per play is a record
of what that household watched written into a file that nothing here can expire.

One line is written per event that faulted, at error level, carrying the
identifier and the exception. That is the only route in the plugin that writes
anything about a play.

Removing the plugin's own data writes a line only where a deletion failed, at
error level, naming the path that is still there, and one warning where the data
folder is the folder the plugin was installed into and the server's own removal
will take it. `docs/plugin-data.md` covers those two paths.

## How much of this a machine holds

A grep refuses the obvious shape, and the suite refuses the shape a grep cannot
see. Together they are less than the rule above.

`no-name-in-a-log-message` in `tools/invariants/rules` refuses a message
placeholder named after a user, an item, a series, an episode, an album, a
client or a device, anywhere in tracked C#. It reads the placeholder rather than
the logging call, because a message template in this tree sits on a line of its
own and a line-based rule reading the call would not reach it. That makes it
stricter than the rule above, which would allow a title at debug level: a grep
cannot see the level from the line the template is on, and refusing both is the
direction that costs a diff rather than a disclosure.

`WhatTheLogContainsTests` runs a whole play through the plugin with the log
captured and asserts that neither the user's name nor the item's title appears
in any line, while the identifiers do. It catches what the grep cannot: a name
passed as the value of a placeholder that is innocently named. That was
measured, by making one handler pass the user name into the existing message and
watching the grep stay green while the test went red.

**What neither of them holds is the level.** The rule above says no item title
above debug; the grep refuses the placeholder at every level and the suite reads
only the one path that logs at all. A title reaching the log through a value
whose placeholder is not in that list, on a path no test drives, would pass
both. Review at the message is the rest of it, and that is a person rather than
a mechanism.

## Deciding your own log retention

Given the above, this plugin's lines age into nothing more sensitive than an
identifier the server has already forgotten and a path on your own disk. It does
not need a shorter log retention than you would otherwise choose. That statement
is about this plugin's lines only; the server itself and every other plugin
write to the same file, and this says nothing about those.
