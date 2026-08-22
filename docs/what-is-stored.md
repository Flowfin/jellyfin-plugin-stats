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

Every other column means what it means in the table above. A row here is not a
play that happened: it is what the server had said about a play up to the last
time it heard from the session.

A row is left behind here when the server stops while something is playing. It
holds the same account and the same item name a finished row does, so every
removal this plugin has reaches it: deleting a user, the sweep for accounts the
server no longer has, a person deleting their own history, and the retention
window all take the running rows along with the finished ones. Nothing yet
turns a leftover row into a finished play, which is issue #221.

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

A daily task covers that one. It reads the accounts the store still holds rows
for, asks the server about each of them, and deletes the rows of the ones the
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

There is no consent. Nothing records whether a user agreed to be counted, and
there is no setting a user can reach. Every play that gets past the four
controls above is recorded, and the plugin has no such state to branch on.
Issue #42 is where that record is built.

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
