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

They exist, they validate, they are stored and the settings page shows them.
None of them changes what the plugin does yet, and this section is here so no
entry below reads as a description of behaviour that is present.

Nothing outside the configuration model and its page reads any of them:

    grep -rn "CaptureEnabled\|PlayRowRetentionDays\|DailyAggregateRetentionDays\|MaximumRangeDays\|MaximumRowsPerResponse\|RollupTimeZone\|ExcludedUserIds\|ExcludedItemTypes" \
      --include=*.cs Jellyfin.Plugin.Stats/ | grep -v "^Jellyfin.Plugin.Stats/Configuration/" ; echo "exit=$?"
    exit=1

with no output. The playback events the plugin subscribes to reach a sink that
discards them:

    grep -n 'AddSingleton<IPlaybackEventSink' Jellyfin.Plugin.Stats/PluginServiceRegistrator.cs
    27:        serviceCollection.AddSingleton<IPlaybackEventSink, DiscardingPlaybackEventSink>();

So no play is recorded, and `CaptureEnabled`, `ExcludedUserIds` and
`ExcludedItemTypes` decide the fate of nothing. There is no retention sweep
either:

    git ls-files Jellyfin.Plugin.Stats/ScheduledTasks/ ; echo "exit=$?"
    exit=0

with no output, so no row is deleted on any schedule and the two retention
windows are read by nothing. There are no reports, so nothing is bounded by
`MaximumRangeDays` or `MaximumRowsPerResponse` and nothing rolls a day up in
`RollupTimeZone`. Issues #29, #39, #32 and #51 are where those gaps close. Until
they do, the entries below say what each setting will govern rather than what it
governs.

## The settings

| setting                       | default | accepted values                                     | what it does                                                                                                                       |
| ----------------------------- | ------- | --------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------- |
| `CaptureEnabled`              | `true`  | `true` or `false`                                    | Whether plays are recorded at all. Off means nothing is written. It does not hide, and does not delete, what is already stored.      |
| `PlayRowRetentionDays`        | `90`    | a whole number of days from `1` to `3650`            | How long a raw play row is kept before the retention sweep deletes it.                                                              |
| `DailyAggregateRetentionDays` | `400`   | a whole number of days from `1` to `3650`            | How long the daily aggregates are kept. Longer than the raw rows on purpose, because they answer a question that names nobody.       |
| `MaximumRangeDays`            | `400`   | a whole number of days from `1` to `3650`            | The widest range a report may ask for. A request for more is refused with the cap named rather than quietly shortened.               |
| `MaximumRowsPerResponse`      | `1000`  | a whole number from `1` to `100000`                  | The most rows any single response may carry.                                                                                        |
| `RollupTimeZone`              | `UTC`   | any zone identifier the running machine can resolve  | The zone a day is counted in. Rows are stored in UTC and read into a local day, so this decides which day a late evening play is on. |
| `ExcludedUserIds`             | empty   | user identifiers                                     | Users whose plays are not recorded. An entry that is not an identifier is dropped and the rest of the list is kept.                  |
| `ExcludedItemTypes`           | empty   | names the server's own item kinds carry              | Item types whose plays are not recorded. An entry the server has no such kind for is dropped and the rest of the list is kept.       |
| `ConfigurationVersion`        | `1`     | written by the plugin                                | Not a setting. It records which shape the stored file is in, and the plugin moves an older file forward before the server reads it.  |
| `RejectedFields`              | empty   | read only                                            | Not a setting and not stored. It names the fields whose stored value was refused, and the page reads it to say which went to default. |

## What takes effect when, and what it leaves alone

This is the meaning each setting is defined to have, which the section above
says is not yet the behaviour of anything.

Nothing here is retroactive. A setting decides what happens from the moment it
is saved, and none of them reaches backwards into what is already stored, with
one exception named below.

Turning `CaptureEnabled` off stops rows being written. Rows already written stay
where they are until their retention window ends or somebody deletes them.

Adding a user to `ExcludedUserIds`, or a type to `ExcludedItemTypes`, is the
same shape: it stops new rows and removes none. A user who wants what was
already recorded gone needs a deletion, which is issue #46.

`MaximumRangeDays` and `MaximumRowsPerResponse` apply to the next request. They
change what a report is allowed to ask for and never what is stored.

`RollupTimeZone` is the exception, and it changes a reading rather than a row. A
stored play does not move; it is stamped in UTC. What moves is which local day
it is counted on, so the same rows can produce different daily totals under a
different zone. An aggregate computed under the old zone is not reused
afterwards, which is issue #50.

The two retention windows take effect on the next sweep rather than on save.
Shortening one does not delete anything at the moment the page is saved.

Whether a saved change reaches the running plugin without a restart is not
stated here, because there is no consumer to reach: the section above shows that
nothing outside the model and its page reads a setting. Issue #72 is where every
consumer is made to read the current value instead of a copy taken at start-up,
and where a setting that turns out to need a restart is named on the page and in
this document rather than left for an operator to discover.

## Retention deletes, and the deletion cannot be undone

The sweep that does this is issue #32 and is not written, so nothing is being
deleted today. What follows is what these two windows mean, and it is here now
because an operator sets a retention before the first sweep runs rather than
after.

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

What survives it is the daily aggregates, for as long as
`DailyAggregateRetentionDays` allows. Those say how much the server was used on
a day and name no user, which is why they are allowed to outlive the rows they
were computed from. Once a day passes that second window, nothing about it is
left.

Setting `DailyAggregateRetentionDays` shorter than `PlayRowRetentionDays` is
accepted and nothing refuses it. It means the aggregates go before the rows they
came from, and a report over a range that far back then has to read the rows
instead. That is a slower report rather than a wrong one, but it is not what the
two windows are shaped for.

## Where this document is checked

`ConfigurationReferenceTests` in the suite reads the table above and compares it
against the configuration model. A field added to the model with no entry here
fails, an entry for a field that no longer exists fails, a default that
disagrees with the code fails, and a range that disagrees with the accepted
range fails.

What it cannot check is whether the prose is right. Every sentence outside the
table is read by a person or not at all.
