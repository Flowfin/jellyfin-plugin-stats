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

Nothing is written while a play is running. The row is assembled as the events
arrive and reaches the store once the play is over, so a session that is still
playing has nothing on disk yet.

The store holds one table, `plays`, and these are its columns.

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
| `PlayMethod`                    | How the server delivered the item: direct play, direct stream, transcode, or unknown.                      |
| `TranscodeVideoCodec`           | The video codec the session ended up using, and empty where the play carried no video.                     |
| `TranscodeAudioCodec`           | The audio codec the session ended up using, and empty where the play carried no audio.                     |
| `TranscodeVideoWasDirect`       | Whether the video stream was passed through for the whole play.                                            |
| `TranscodeAudioWasDirect`       | Whether the audio stream was passed through for the whole play.                                            |
| `TranscodePeakBitrate`          | The highest bitrate observed over the play, in bits per second, and empty where none was reported.         |
| `TranscodeTypicalBitrate`       | The bitrate the play spent most of its samples at, in bits per second, and empty where none was reported.  |
| `TranscodeHardwareAcceleration` | The hardware acceleration the server reported, and empty where it reported none.                           |
| `TranscodeReasons`              | Every transcode reason observed over the play, without repeats, as the server reported them at the time.   |

`WhatIsStoredTests` compares that first column against the statement the store
runs to create the table, so a column added to the schema without an entry here
is a red test rather than a paragraph somebody has to remember to update.

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
which fires on a near miss of its own.

## Who can read it

Nothing in this plugin serves any of it. There is no route into the store from
outside the server process, because the plugin has no endpoint at all:

    git grep -lE "ControllerBase|ApiController|HttpGet|HttpPost" -- '*.cs'
    tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs

The single hit is the near miss belonging to a rule, under `tools/invariants`,
and it is not compiled into the plugin. The settings page reads settings and
shows no stored row.

So the readers of this data are whoever can read the file. That is the server's
own account and anyone with access to the server data directory or to a backup
of it, and `docs/plugin-data.md` names the two paths. A user of the server
cannot read their own rows, and an administrator cannot read anybody's through
the plugin either.

There is also no route out. This plugin reads the server it runs inside and
talks to nothing outside it, and the client that would make such a call is
refused in the source by `no-outbound-http-client` in `tools/invariants/rules`.

## What an administrator controls

Four settings on the plugin's settings page decide what is recorded and how long
it stays. `docs/configuration.md` is the reference for what each accepts and
what it defaults to; this says only which ones reach the data.

- `CaptureEnabled` stops rows being written. It is read immediately before the
  write rather than in a report, so switching it off stops the recording and not
  merely the display.
- `ExcludedUserIds` stops rows being written for named users. This is an
  administrator excluding somebody, not somebody excluding themselves.
- `ExcludedItemTypes` stops rows being written for named kinds of item.
- `PlayRowRetentionDays` decides how old a row may get before the retention
  sweep deletes it. The sweep runs daily, can be started by hand from the
  server's scheduled task list, and the deletion is permanent.

None of the four reaches back over rows that are already stored, apart from the
retention sweep, which deletes them.

## What this plugin does not have yet

A statistics plugin is usually expected to offer these, and this one does not.
They are absences rather than controls, and none of them should be read as
partly present.

There is no consent. Nothing records whether a user agreed to be counted, and
there is no setting a user can reach. Every play that gets past the four
controls above is recorded, and the plugin has no such state to branch on.
Issue #42 is where that record is built.

A signed in user cannot read their own history, export it, or delete it. There
is no endpoint for any of that, which is issues #43 and #46.

Deleting a user from the server does not delete their rows. The plugin does not
watch for a user being removed, and nothing sweeps the store for identifiers the
server no longer knows, so a deleted user's plays stay in the store until their
retention window expires or the plugin is removed. Issues #44 and #45.

Until those land, the whole of what an administrator can do about one person's
data is to exclude them from future capture, shorten the retention window for
everybody, or delete the store file.

## What this document does not cover

Where the files are and what an uninstall removes are in `docs/plugin-data.md`.
What reaches the server log is in `docs/what-the-log-contains.md`. What each
setting accepts is in `docs/configuration.md`. This document adds the contents
and the readers, which is the part those three deliberately leave out.
