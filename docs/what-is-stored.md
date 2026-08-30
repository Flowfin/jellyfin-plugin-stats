# What this plugin records, and who can read it

Installing this plugin means taking on responsibility for data about the people
using the server. That is only possible if the data can be read without reading
the source, so this document is the account of it: every field kept about a
play, the fields refused on purpose, who can reach any of it, and which of the
promises a statistics plugin usually makes this one does not make yet.

Two neighbouring documents are referenced rather than repeated.
`docs/plugin-data.md` says where the files are and what an uninstall takes away.
`docs/what-the-log-contains.md` says what reaches the server log, which is a
different place with different readers. Nothing about the log is restated here.

## One row per finished play

A play is written twice over its life and is one row at every moment of it.
While it is running it is a row in `open_plays`, rewritten in place as the
server reports; when it stops, that row is taken away and a row in `plays`
takes its place, both in the same act so the play is never two rows and never
none.

What a play costs the file therefore does not move with how often its session
reported. A three hour film whose client checks in every ten seconds is one
row while it plays and one row afterwards, the same as a play that reported
twice.

These are the columns of `plays`, which is the table every report is built on.

| column                          | holds                                                                                                    |
| ------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `Id`                            | The row's own number, given by the store. It says nothing about the play.                                  |
| `SchemaVersion`                 | The row shape this row was written under, so a reader can tell what it is holding.                         |
| `UserId`                        | The user the play belongs to, as the server's identifier for them. This is the personal detail in the row. |
| `ItemId`                        | The item that was played, as the server's identifier for it.                                               |
| `ItemType`                      | The kind of item, as the server reported it at the time of the play.                                       |
| `ParentId`                      | The series or album the item belongs to, and empty where it belongs to none.                               |
| `ItemName`                      | The item's name at the time of the play, kept so a report over items the library no longer has still reads. |
| `ItemRuntimeTicks`              | How long the item was, and empty where the item had no runtime.                                            |
| `StartedUtcTicks`               | When the play started, in UTC.                                                                             |
| `EndedUtcTicks`                 | When the play ended, in UTC.                                                                               |
| `WatchedDurationTicks`          | How much of the item was watched, which is not the difference between the two times above.                 |
| `ReachedTheEnd`                 | Whether the play reached the end of the item.                                                              |
| `ClientName`                    | The client application the play came from.                                                                 |
| `DeviceId`                      | The server's identifier for the device the play came from.                                                 |
| `DeviceName`                    | The device's reported name, which is whatever its owner called it.                                         |
| `PlayMethodAtStart`             | How the server was delivering the item when the play began: direct play, direct stream, transcode, or unknown. |
| `PlayMethodChangedUtcTicks`     | When the server first reported a different delivery method, and empty where it never did. A row written before this column existed is empty here whatever the play did. |
| `TranscodeVideoCodec`           | The video codec the session ended up using, and empty where the play carried no video.                     |
| `TranscodeAudioCodec`           | The audio codec the session ended up using, and empty where the play carried no audio.                     |
| `TranscodeVideoWasDirect`       | Whether the video stream was passed through for the whole play.                                            |
| `TranscodeAudioWasDirect`       | Whether the audio stream was passed through for the whole play.                                            |
| `TranscodePeakBitrate`          | The highest bitrate observed over the play, in bits per second, and empty where none was reported.         |
| `TranscodeTypicalBitrate`       | The bitrate the play spent most of its samples at, in bits per second, and empty where none was reported.  |
| `TranscodeHardwareAcceleration` | The hardware acceleration the server reported, and empty where it reported none.                           |
| `TranscodeReasons`              | Every transcode reason observed over the play, without repeats, as the server reported them at the time.   |
| `ClosedBy`                      | Which route ended the play: a stop from the server, the session ending, the session going quiet, or a later start-up finishing what was left running. Empty where the row does not say, which is every row written before this column existed. |
| `ChannelName`                   | The channel a live television play was on, as the library called it at the moment the play was recorded. Empty for every play that was not live television, for a channel the library no longer held, and for every row written before this column existed. It is the name rather than the identifier, because a live channel is renamed and taken off the air while the rows a report is about stay where they are. |

## One row per play that is still running

`open_plays` is where a play sits between its start and its stop. Its columns
are the ones above with the row's own number replaced by the key the server's
events are joined on, and two of them are read differently because a running
play has not answered them yet.

| column          | holds                                                                                                                  |
| --------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `PlayKey`       | What the server's events for this play are joined on, and the identity of the row. Writing the same key again replaces it. |
| `EndedUtcTicks` | The last moment the server heard from the session, which moves forward as the play runs. It is not a claim that the play ended. |
| `ReachedTheEnd` | Always false here. Nothing has said the item was played through, and the server only says so on the stop.                 |
| `PlayMethodChangedUtcTicks` | The same field as above, filled in as soon as the server reports a different method rather than only at the stop.        |
| `ClosedBy`      | Always the not-said value here. The play has not been closed, so no route has ended it, and the value it will carry is decided when one does. |
| `ChannelName`   | The same field as above. It is filled in on the start event, so a running live television play names its channel from the first row it writes. |

Every other column means what it means in the table above. A row here is not a
play that happened: it is what the server had said about a play up to the last
time it heard from the session.

A row is left behind here when the server stops while something is playing. It
holds the same account and the same item name a finished row does, so every
removal this plugin has reaches it: deleting a user, the sweep for accounts the
server no longer has, a person deleting their own history, and the retention
window all take the running rows along with the finished ones.

A leftover row does become a finished play. The next start-up finishes every row
this table still holds, before anything subscribes to the server's events, and
the finished row records that a restart is what ended it. The row is what the
previous process last wrote, so its end is the last moment that server heard from
the session and nothing after that moment is invented.

### Two fields about two moments

`PlayMethodAtStart` and the transcode columns are about different moments and
read as one answer, which is the confusion `PlayMethodChangedUtcTicks` exists to
end.

The delivery method is taken once, off the state the session was in when
playback started. The transcode columns are folded from every sample that
arrived while the play ran. So a play that begins as a direct play and is
re-encoded from its second minute has a start method saying direct play and a
summary saying the video was not direct, and both are true statements about
different parts of the same play.

`PlayMethodChangedUtcTicks` is when the two parted company. Empty means the
server never reported another method, and the start value then describes the
whole play. It records the first such moment rather than the last, because what
a reader needs is whether the start value still described the play and from when
it did not.

### What ended the play, and why a report says so

`ClosedBy` is the other field a figure cannot be read without. Only one of its
routes is the server saying the play is over. On the other three nothing said
so: the session ended, or it went quiet for longer than the plugin waits, or the
server stopped and a later start-up finished what was left running.

The difference is in the numbers rather than in the wording. A play the server
sent a stop for has a watched duration that is what was watched. On the other
three it is what had been watched by the last moment the server heard from the
session, which is a floor and not a total. So a report that adds watched time
over a range says how many of the plays it read ended cleanly beside it, and a
reader can tell how much of the figure is a floor.

Empty means the row does not say. That is every row written before this column
existed, and it is counted on its own rather than as either kind, because
counting an absence as a clean ending would claim something the row does not
say. That figure falls as those rows age out of the retention window.

A sample the server gave no delivery method for leaves it empty, the same way a
sample it gave no transcoding state for leaves the summary alone: the server
having nothing to say about a session is not the session having changed.

`WhatIsStoredTests` compares the first column of each table above against the
columns the schema steps leave that table with, so a column added to either
schema without an entry here is a red test rather than a paragraph somebody has
to remember to update. It walks the steps rather than reading the create
statement alone, because a column added by a later step is on every
installation's disk and would otherwise be invisible to the comparison.

Two of these are personal in the ordinary sense and the rest are not.
`UserId` names a person, indirectly but exactly. `DeviceName` is whatever the
owner of the device typed into it, and people put their own names in there. The
others describe what was played and how it was delivered.

## One row per account that has been asked about being named

`consents` holds what each account has said about an administrator seeing its
plays as that account's. It is a separate question from whether the rows are
kept: the rows are kept either way, and the wording a person is shown says so in
its own words.

| column              | holds                                                                                                          |
| ------------------- | ---------------------------------------------------------------------------------------------------------------- |
| `UserId`            | The account this record is about, and the identity of the row. There is one answer at a time.                      |
| `Agreed`            | Whether the account is agreeing as things stand. A record exists for an account that has withdrawn as well as for one that is agreeing, so its presence says the question was answered rather than answered yes. |
| `AgreedUtcTicks`    | When the account last agreed, and empty where it never has.                                                        |
| `WithdrawnUtcTicks` | When the account last withdrew, and empty where it never has. A withdrawal keeps the agreement it withdraws beside it, because an account that agreed in March and withdrew in July has said two things. |
| `WordingVersion`    | The version of the wording the account was shown when it last agreed, and nought where it has never agreed. No wording is version nought. |

The version is what stops a later change to the words inheriting an old
agreement. `Jellyfin.Plugin.Stats/Privacy/consent.txt` carries the words and the
number at the top of the file, the assembly reads both, and an agreement naming
any other version is refused rather than recorded. So an agreement always names
words that exist, and words that have moved on leave an agreement a reader can
see is about the older ones.

Only the account itself writes this. An administrator cannot set it and cannot
read it, which is one of the rows in the authorization matrix named above, and a
consent an administrator could record is not consent.

The record goes when the account does. Deleting a user takes what that user said
along with their rows, because an account the server no longer has has nobody
left to have answered. A person deleting their own history keeps their answer:
they are still here, and the answer is still theirs.

The daily task described below covers the deletion this plugin did not see. It
reaches an account holding a record and no plays as well as one holding both,
because a record is a place the store names an account and the plays are
another, and the two sets are not the same one: an account that answered the
question and then watched nothing is only in the first.

## One row per day, account, kind of item and client

`daily_rollups` holds the day-by-day account of what was played, folded as the
rows are written rather than counted again on every request. Nothing in it is a
fact this plugin does not already hold: every column is one a play row carries
or one that follows from the play rows alone, so the table can be produced again
from the rows underneath it and compared against them.

| column                 | holds                                                                                                                    |
| ---------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `Day`                  | The calendar day, written as an ISO date, in the zone the table states below. Part of the identity of the row.              |
| `UserId`               | The account the plays behind the row belong to. Part of the identity of the row.                                           |
| `ItemType`             | The kind of item they were of. Part of the identity of the row.                                                            |
| `ClientName`           | The client they were played on. Part of the identity of the row.                                                           |
| `Plays`                | How many plays the row stands over.                                                                                        |
| `WatchedDurationTicks` | How long was watched across them, added up. Ticks, like every other duration here, so a rebuild adds the same numbers rather than rounded ones. |
| `Completed`            | How many of those plays reached the end of the item.                                                                       |
| `UnknownMethod`        | How many of them began with no delivery method reported.                                                                   |
| `DirectPlay`           | How many of them began as a direct play.                                                                                   |
| `DirectStream`         | How many of them began as a direct stream, which is the case a report calls remuxed.                                       |
| `Transcode`            | How many of them began as a transcode.                                                                                     |

The four delivery counts add up to `Plays`, because a play adds one to exactly
one of them. Transcoded is one column and direct is the sum of two, and folding
them here would have lost the difference between a play the server repackaged
and one it re-encoded.

`rollup_zone` holds one row and one column, `ZoneId`, and it is the zone every
day above was counted in. Which day a play falls on is not a fact about the
play: a play at half past eleven at night belongs to a different day depending
on whose midnight is meant. The table states one answer for itself rather than
each reader assuming one.

A store states the zone it first kept a rollup in, and keeps stating it. Moving
the setting afterwards does not move these rows, because rekeying them is a
rebuild of the table rather than a setting taking effect, and a file holding one
day keyed in two zones is a file nothing can read as a day.

**This table is empty on a store that already held plays before it arrived.** It
is filled as rows are written, so an upgrade brings the table and none of the
days the store already has. What produces those is a rebuild, which reads the
play rows a page at a time and folds them again from scratch, throwing away every
row here first. Nothing reads this table yet; the report that will is #254.

The rebuild is what makes this table derived rather than authoritative. A table
that cannot be produced again from the rows underneath it is the only copy of
what it holds, and one that has drifted from them is worse than no table at all,
because it is believed. Both are read the same way: rebuild and compare.

**A rebuild produces the days the store still has rows for, and no others.** On a
store the retention sweep has aged rows out of, the days those rows were in are
in this table and not in the play rows, so a rebuild there throws away exactly
the figures the sweep deliberately left standing. That is why a rebuild is an
operation somebody asks for rather than one anything runs on a schedule.

**A corrective deletion moves these rows and a retention deletion does not.** The
two are opposite statements about the same play rows, and which one a deletion
was is written down beside it in the table below. A corrective deletion takes
each removed play back out of the day it was folded into, before the row goes,
inside the same transaction, and a day nothing is left in stops being a day
rather than standing at nought. A retention deletion leaves every figure here
where it is, which is what the longer aggregate window exists for: the daily
sweep at the default ninety days would otherwise empty aggregates about three
hundred days before their own expiry, on every installation running defaults.

## One row per deletion that removed something

`deletions` holds what each deletion this plugin performed said about the rows
it took, and nothing about the rows themselves.

| column  | holds                                                                                                        |
| ------- | ------------------------------------------------------------------------------------------------------------ |
| `Id`    | The order the entries were written in, which is the only order this table has. |
| `Class` | What the deletion said: that the rows aged out of the retention window, or that the plays stop being counted.  |
| `Rows`  | How many rows that call removed. Always more than nought.                                                      |

The two classes are opposite statements about the same rows. Retention says this
plugin's copy of them has aged out and every figure computed while they were
there still stands; a deleted account, or somebody removing their own history,
says those plays stop being counted, so a figure that counted them has to move.
Nothing in the rows separates the two, and the rows are gone by the time anybody
asks, which is why the answer is written here at the moment of the deletion
rather than worked out afterwards.

It holds no account, no item and no moment. What a later reader needs from it is
which class each deletion was and in what order, so there is nothing here that
says whose history was removed - an entry survives the rows it is about, and a
column naming the person would outlive them too.

There is no moment either. The store names no clock, so a timestamp here would be
a fact about the machine the deletion ran on rather than about the deletion, and
it would disagree with the rows on any server whose clock has moved.

**This table is empty on a store that already held plays before it arrived.**
The deletions such a store performed were made by builds that recorded no class,
and filling them in would invent the answer this table exists to stop being
guessed.

Nothing reads the table back yet. What acts on the class is the deletion itself:
a corrective one reaches the day-by-day rollups above and a retention one leaves
them standing, both decided from the argument the call carried rather than from
this table. What this holds is the account a later reader has of which deletions
were which, and on a store upgraded from an earlier build that account starts at
the day the table arrived.

## What is refused on purpose

Three things a playback session carries are absent from the row and from the
schema, because they turn playback statistics into a record of where somebody
was and what their disk holds.

- The network endpoint the session came from.
- The client's user agent string.
- The item's location on disk.

They are refused in the source rather than dropped afterwards, by
`no-identifying-network-or-path-detail-in-the-store` in `tools/invariants/rules`,
which fires on a near miss of its own. Neither table carries any of them.

## Who can read it

Two routes reach the store from outside the server process, and both of them
serve one account itself:

- `GET /Stats/Users/{userId}/Years/{year}` answers with that account's own
  calendar year.
- `DELETE /Stats/Users/{userId}/Plays` removes that account's own plays, all of
  them or those that started inside a window the request names.

Both refuse a request naming any account other than the one that made it, and an
administrator is refused by the same line as anybody else. There is no elevated
route to one person's history here, which is the whole of what this plugin has
to say about who may reach it, and it is a table rather than a sentence:
`Jellyfin.Plugin.Stats.Tests/AuthorizationMatrixTests.cs` carries every endpoint
crossed with four callers, refuses an endpoint that has no row in it, and
refuses one that has stopped carrying an authorization attribute.

No page in this plugin sends either request. The settings page reads settings
and shows no stored row, so a person reaching either route today addresses it by
hand.

The year answer is cut to what the account asking may still see. Every label in
it comes off the row that was written when the play happened, and the library is
asked one thing while the request is served: whether that account may see the
item a row of a top list would name. An item it may not see is dropped from the
lists and still counted in the totals, so a figure never moves for a reason a
reader cannot see; an item the library no longer holds is named, because a play
of something that has since been deleted is still that account's own play and
there is no access question left to ask about it. The rows are untouched by any
of this: what changes is the answer, per request, and never the store.

Beyond those two, the readers of this data are whoever can read the file. That is
the server's own account and anyone with access to the server data directory or
to a backup of it, and `docs/plugin-data.md` names the two paths.

There is also no route out. This plugin reads the server it runs inside and
talks to nothing outside it, and the client that would make such a call is
refused in the source by `no-outbound-http-client` in `tools/invariants/rules`.

## What an administrator controls

Four settings on the plugin's settings page decide what is recorded and how long
it stays. `docs/configuration.md` is the reference for what each accepts and
what it defaults to; this says only which ones reach the data.

- `CaptureEnabled` stops rows being written. It is read immediately before the
  write rather than in a report, so switching it off stops the recording and not
  merely the display. Switching it off while something is playing also takes
  away the row that play already has, so the change reaches what is on the file
  because of it and not only what would have been.
- `ExcludedUserIds` stops rows being written for named users, running and
  finished alike. This is an administrator excluding somebody, not somebody
  excluding themselves.
- `ExcludedItemTypes` stops rows being written for named kinds of item.
- `PlayRowRetentionDays` decides how old a row may get before the retention
  sweep deletes it. The sweep runs daily, can be started by hand from the
  server's scheduled task list, and the deletion is permanent. It takes a
  running row that started before the same cutoff as well, which on a server
  that has been up for less than the window is a row no session is behind.

None of the four reaches back over rows that are already stored, apart from the
retention sweep, which deletes them.

Two things that are not settings reach back over them as well, and an
administrator switches neither on and can switch neither off.

Deleting a user from the server deletes every row belonging to that user, at the
moment the server publishes the deletion, and then gives the space they were
using back to the file so the bytes are gone from it rather than sitting in a
page nothing points at. What it cannot cover is a user deleted while this plugin
was not loaded, because a plugin that is not running hears nothing.

A daily task covers that one. It reads every account the store still names -
those it holds rows for and those it holds a consent record for - asks the
server about each of them, and deletes the rows and the record of the ones the
server does not have any more, then gives that space back the same way. It is in
the server's scheduled task list as "Delete playback statistics belonging to
accounts the server no longer has", so an administrator can also run it by hand,
and the deletion is as permanent as the other two. An account the server cannot
be asked about, because the lookup failed rather than because it is gone, keeps
its rows: the task asks about every identifier before it deletes anything, so a
failure costs a run rather than somebody's history.

## What this plugin does not have yet

A statistics plugin is usually expected to offer these, and this one does not.
They are absences rather than controls, and none of them should be read as
partly present.

Consent is recorded and nothing reads it yet. An account can say whether it may
be named and read back what it said, and the record is the table above; what
does not exist is any view that would show one person's plays as theirs, so
there is nothing yet for an agreement to permit or a withdrawal to stop. Issue
#42 stays open on that half.

Every play that gets past the four controls above is recorded whatever the
account said, which is what the wording tells the person reading it.

A signed in user cannot export their own history. Reading their own year and
deleting their own plays are the two routes named above; there is no endpoint
that hands somebody a copy of their rows to keep.

Nor is there a page for any of it. The two routes exist and nothing in the
server's interface offers them, so what a person can do about their own history
today they can do only by addressing the route themselves.

Nothing reports what the sweep for forgotten accounts removed. The count goes to
the server's task list as the run's own result and to nothing that keeps it, and
this plugin deliberately writes no line about it to the server log, for the
reason `docs/what-the-log-contains.md` gives: how many rows a server was holding
for accounts it no longer has is a statement about who used to watch what, and
the log outlives every retention setting here. So an administrator learns that
rows were removed by watching the task run, and not afterwards.

Until the rest of those land, what an administrator can do about one person's
data is to delete their account, exclude them from future capture, shorten the
retention window for everybody, or delete the store file. Only the first of
those is about one person and it takes their account with it.

## What this document does not cover

Where the files are and what an uninstall removes are in `docs/plugin-data.md`.
What reaches the server log is in `docs/what-the-log-contains.md`. What each
setting accepts is in `docs/configuration.md`. This document adds the contents
and the readers, which is the part those three deliberately leave out.
