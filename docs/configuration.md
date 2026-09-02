# Configuration reference

Every setting this plugin has: what it does, what it accepts, what it defaults
to, and what changing it does not do.

The settings page is where these are edited. The server keeps them in its own
plugin configuration file and hands the whole set to the plugin as one object,
so a value edited by hand in that file goes through the same checks as one typed
into the page.

A value outside what a setting accepts is refused rather than clamped, and the
field falls back to its default. Refusing is deliberate: a retention of eleven
thousand days clamped to ten years is a setting that means something other than
what it says, and the operator is never told. The page names the fields that
happened to, above the form.

## What these settings govern today

All eight of them decide something. One of the eight reached nothing until
issue #315 built the sweep it names, and this section says where each is read
so no entry below has to be taken on trust.

`CaptureEnabled`, `ExcludedUserIds` and `ExcludedItemTypes` are honoured
immediately before a play is written:

    grep -n 'configuration.CaptureEnabled\|configuration.ExcludedUserIds\|configuration.ExcludedItemTypes' \
      Jellyfin.Plugin.Stats/Capture/CaptureGate.cs
    94:        if (!configuration.CaptureEnabled)
    99:        if (Array.Exists(configuration.ExcludedUserIds, entry => Guid.Parse(entry) == play.UserId))
    105:            configuration.ExcludedItemTypes,

`PlayRowRetentionDays` decides what the retention sweep deletes, and it is read
at the run rather than held from start-up:

    grep -rn 'PlayRowRetentionDays' --include=*.cs Jellyfin.Plugin.Stats/ScheduledTasks/
    Jellyfin.Plugin.Stats/ScheduledTasks/RetentionSweepTask.cs:117:        var cutoff = now.AddDays(-configuration.PlayRowRetentionDays);

`RollupTimeZone` decides which local day a play is counted on, and every route
that answers a report reads it at the request:

    grep -rn 'RollupTimeZone' --include=*.cs Jellyfin.Plugin.Stats/Api/
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:344:        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:419:        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs:148:        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:176:        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.RollupTimeZone);
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:240:        var zone = TimeZoneInfo.FindSystemTimeZoneById(_configuration().RollupTimeZone);

`MaximumRangeDays` and `MaximumRowsPerResponse` bound what the aggregate report
routes answer, read at the request rather than held:

    grep -n 'MaximumRangeDays\|MaximumRowsPerResponse' \
      Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs
    502:                longestRange: TimeSpan.FromDays(_configuration().MaximumRangeDays));
    539:    private bool WithinTheRowCap(int rows) => rows <= _configuration().MaximumRowsPerResponse;

That was issue #305, and it is worth stating what it moved, because an
installation that stored the previous default is not where it was. Until it
landed the two caps reached nothing: the range every route answered over was
367 days whatever the page said, the number of rows a response carried was
whatever the fold produced, and an operator who lowered either was told nothing.
The range ceiling on these routes is now the operator's number rather than the
query layer's, and the shipped default moved from 400 to 367 so an installation
nobody has configured is bounded by exactly what bounded it before. A file
already carrying 400 answers over up to 400 days from now on, which is five
weeks wider than it was.

What is NOT operator-settable is the bound that stops one request making the
server do arbitrary work, and it is the play count rather than either cap:

    grep -n 'MostPlaysAnyShapeReads =' Jellyfin.Plugin.Stats/Reports/QueryWindow.cs
    52:    public const int MostPlaysAnyShapeReads = 250_000;

The third is read by the same sweep, at the same run, and it is the setting
this section used to record as reaching nothing:

    grep -rn "DailyAggregateRetentionDays" \
      --include=*.cs Jellyfin.Plugin.Stats/ | grep -v "^Jellyfin.Plugin.Stats/Configuration/"
    Jellyfin.Plugin.Stats/ScheduledTasks/RetentionSweepTask.cs:118:        var aggregateCutoff = now.AddDays(-configuration.DailyAggregateRetentionDays);

WHAT STOOD HERE SAID THE COMMAND ABOVE RETURNED NOTHING, and until issue #315
landed it did. A daily aggregate was kept for ever whatever the number on the
page said, while the raw rows behind it went at ninety days by default, which is
the opposite of the sentence the two settings carry about the two windows.
Three statements remove a rollup row now rather than two, and the third is the
one that reads an age:

    grep -rn 'DELETE FROM daily_rollups' --include=*.cs Jellyfin.Plugin.Stats/
    Jellyfin.Plugin.Stats/Data/SqlitePlayStore.cs:500:        "DELETE FROM daily_rollups WHERE Plays <= 0";
    Jellyfin.Plugin.Stats/Data/SqlitePlayStore.cs:524:        @"DELETE FROM daily_rollups
    Jellyfin.Plugin.Stats/Data/SqlitePlayStore.cs:535:    private const string ForgetEveryRollup = "DELETE FROM daily_rollups";

The first of the three still drops a day a corrective deletion emptied and the
last still clears the table for a rebuild. Neither of those is a window; the
middle one is, and it is the statement the sweep bites through.

## The settings

| setting                       | default | accepted values                                     | what it does                                                                                                                       |
| ----------------------------- | ------- | --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `CaptureEnabled`              | `true`  | `true` or `false`                                    | Whether plays are recorded at all. Off means nothing is written. It does not hide, and does not delete, what is already stored.      |
| `PlayRowRetentionDays`        | `90`    | a whole number of days from `1` to `3650`            | How long a raw play row is kept before the retention sweep deletes it.                                                              |
| `DailyAggregateRetentionDays` | `400`   | a whole number of days from `1` to `3650`            | How long a daily aggregate is kept before the same retention sweep deletes it. A day older than this is gone from the figures.       |
| `MaximumRangeDays`            | `367`   | a whole number of days from `1` to `3650`            | The widest range an aggregate report may ask for. A longer range is refused rather than quietly shortened.                           |
| `MaximumRowsPerResponse`      | `1000`  | a whole number from `1` to `100000`                  | The most rows an aggregate report may carry. An answer with more rows is refused rather than cut to the first of them.               |
| `RollupTimeZone`              | `UTC`   | any zone identifier the running machine can resolve  | The zone a day is counted in. Rows are stored in UTC and read into a local day, so this decides which day a late evening play is on. |
| `ExcludedUserIds`             | empty   | user identifiers                                     | Users whose plays are not recorded. An entry that is not an identifier is dropped and the rest of the list is kept.                  |
| `ExcludedItemTypes`           | empty   | names the server's own item kinds carry              | Item types whose plays are not recorded. An entry the server has no such kind for is dropped and the rest of the list is kept.       |
| `ConfigurationVersion`        | `1`     | written by the plugin                                | Not a setting. It records which shape the stored file is in, and the plugin moves an older file forward before the server reads it.  |
| `RejectedFields`              | empty   | read only                                            | Not a setting and not stored. It names the fields whose stored value was refused, and the page reads it to say which went to default. |
| `WhyTheStoreCouldNotBeOpened`  | empty   | read only                                            | Not a setting and not stored. It names the failure the plugin met opening its store, and is empty where nothing has failed to open it. |
| `OldestStoredPlay`            | empty   | read only                                            | Not a setting and not stored. When the oldest play still held started. Empty means the store holds none, or that it could not be read. |

## What takes effect when, and what it leaves alone

This is the meaning each setting is defined to have. For the one the section
above names as read by nothing, it is still a definition rather than a
description.

Nothing here is retroactive. A setting decides what happens from the moment it
is saved, and none of them reaches backwards into what is already stored, with
one exception named below.

Turning `CaptureEnabled` off stops rows being written. Rows already written stay
where they are until their retention window ends or somebody deletes them.

Adding a user to `ExcludedUserIds`, or a type to `ExcludedItemTypes`, is the
same shape: it stops new rows and removes none. A user who wants what was
already recorded gone deletes it, which is a route of its own rather than a
setting:

    grep -n 'Route("Stats/Users/{userId}/Plays")\|HttpDelete' \
      Jellyfin.Plugin.Stats/Api/YourHistoryController.cs
    44:[Route("Stats/Users/{userId}/Plays")]
    108:    [HttpDelete]

`MaximumRangeDays` and `MaximumRowsPerResponse` apply to the next request,
changing what an aggregate report may ask for and carry, and never what is
stored. Both are read while the request is served, so a saved page binds the
request after it rather than the restart after it. Both refuse rather than
shorten: a report folded from the part of a range that fitted, or cut to the
first of its rows, reads exactly like one that covered the whole of what was
asked.

`RollupTimeZone` is the exception, and it changes a reading rather than a row. A
stored play does not move; it is stamped in UTC. What moves is which local day
it is counted on, so the same rows can produce different daily totals under a
different zone. An aggregate computed under the old zone is not reused
afterwards: the store states the zone its rollups were keyed in, and a report
asked for in any zone with other rules folds the play rows instead of the
rollups.

    grep -n 'RollupZone is not TimeZoneInfo keyed' \
      Jellyfin.Plugin.Stats/Reports/AggregateQueries.cs
    926:        if (store.RollupZone is not TimeZoneInfo keyed || !keyed.HasSameRules(zone))
    1039:        if (store.RollupZone is not TimeZoneInfo keyed || !keyed.HasSameRules(zone))

`PlayRowRetentionDays` and `DailyAggregateRetentionDays` take effect on the next
sweep rather than on save. Shortening either deletes nothing at the moment the
page is saved. Both are read at the run, off one reading of the clock, so a
sweep cannot measure its two boundaries from different days.

None of the eight settings needs a restart. Each is read at the
moment it is used rather than copied at start-up: capture and the two exclusion
lists at every play, both retention windows at every sweep, and the rollup zone
and the two report caps at every request for a report. A setting that turns out to need a restart is named
on the page and in this document rather than left for an operator to discover.

## Retention deletes, and the deletion cannot be undone

The sweep runs daily and can also be started by hand, from the scheduled tasks
page of the server, where it is called "Delete playback statistics past their
retention windows". It reads both `PlayRowRetentionDays` and
`DailyAggregateRetentionDays`, and it is one task over two windows rather than
two tasks: a second task on the same schedule and the same store is how two
windows drift apart until one is quietly not running.

`PlayRowRetentionDays` and `DailyAggregateRetentionDays` are not display
filters. A row past its window is deleted from the plugin's store, and the
plugin keeps no second copy of it. There is no undo. A window shortened by
accident and corrected an hour later does not bring back what the sweep removed
in between.

The one thing a row can be restored from is an export taken before it went, and
the plugin takes none by itself. The export and the import exist as code:

    git ls-files Jellyfin.Plugin.Stats/Data/PlayArchive.cs
    Jellyfin.Plugin.Stats/Data/PlayArchive.cs

and nothing calls either of them on a schedule or from a page, so an
administrator who wants a copy has to have made one deliberately.

What is deleted when a raw play row passes `PlayRowRetentionDays` is the row
itself: which user played which item, when it started and stopped, how much was
watched, the client and device it played on, and whether the server transcoded
and why.

The sweep also gives the space those rows were using back to the disk, at the
end of a run rather than after each deletion. A store file that does not shrink
after a sweep is one that found nothing to delete, or one whose run was
cancelled before it got that far.

What survives a row is the daily aggregates, and that is deliberate rather than
incidental. A retention deletion leaves the day the row was folded into alone,
which is what separates it from a deletion correcting the record; that second
kind does take the row back out of its day:

    grep -n 'A retention deletion reaches none of this' \
      Jellyfin.Plugin.Stats/Data/SqlitePlayStore.cs
    1511:    // A retention deletion reaches none of this. Its statement is that the raw

Those figures say how much the server was used on a day and name no user, which
is why they are allowed to outlive the rows they were computed from.

They outlive them by the difference between the two windows, and no longer.
`DailyAggregateRetentionDays` is the second window closing behind the first: a
day keyed before it is deleted on the next sweep, and the figures a report
reads stop covering that day. What is deleted there is the day's totals - how
many plays, how long, how many finished, and how the server delivered them -
per account, item type and client. No play row is deleted with it and no name
is in it.

WHICH OF THE TWO NUMBERS IS LARGER DECIDES WHETHER THE AGGREGATE DELETION CAN BE
UNDONE, and nothing on the page says so, which is why it is said here.

- `DailyAggregateRetentionDays` larger than `PlayRowRetentionDays`, which is the
  shipped arrangement: an aggregate outlives the rows it was folded from, so
  when it goes it is the only remaining record of that day and the deletion is
  terminal.
- `DailyAggregateRetentionDays` smaller than or equal to `PlayRowRetentionDays`:
  the rows are still in the store when the aggregate goes, so the day can be
  folded again by a rebuild, and until it is, a report over that range reads the
  rows instead - a slower report rather than a wrong one.

Both are accepted and nothing refuses either. One sweep serves both windows and
the aggregates go first inside it, so a run stopped halfway leaves the second
case recoverable rather than making it terminal by taking the rows away first.

## Where this document is checked

`ConfigurationReferenceTests` in the suite reads the table above and compares it
against the configuration model. A field added to the model with no entry here
fails, an entry for a field that no longer exists fails, a default that
disagrees with the code fails, and a range that disagrees with the accepted
range fails.

One of the four reaches outside the table. It holds that the retention section
names both windows, says what is deleted, says what survives, and says the
deletion is permanent:

    grep -n 'public void ' Jellyfin.Plugin.Stats.Tests/ConfigurationReferenceTests.cs
    62:    public void TheTableCoversExactlyTheFieldsTheModelHas()
    77:    public void EveryEntryNamesTheDefaultTheModelStartsFrom()
    106:    public void EveryBoundedEntryNamesTheRangeTheSetterEnforces()
    144:    public void TheRetentionSectionSaysWhatGoesWhatStaysAndThatItIsPermanent()

What that one holds is that the statements are present, not that they are
right. A section saying the wrong thing in the right words passes it, and every
sentence this pass repaired had passed it. Run against the text this pass
replaced, all four are green:

    DOTNET_CLI_UI_LANGUAGE=en dotnet test --nologo -v q --filter "FullyQualifiedName~ConfigurationReferenceTests"
    Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4  [net9.0]
    Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4  [net10.0]

with the duration and the assembly path of each line cut, because both move.

Every other sentence outside the table is read by a person or not at all.
