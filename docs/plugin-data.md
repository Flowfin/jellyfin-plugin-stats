# Where this plugin keeps its data, and what removing it takes away

Two places on the server hold something this plugin wrote. Both are named here
so an administrator can look at them, back them up, or check after an uninstall
that they are gone.

`<server data directory>/plugins/Jellyfin.Plugin.Stats`

The plugin's own data folder. Everything the plugin writes for itself lives
here. The server hands this path to the plugin and takes the name from the
assembly, so it does not change with the plugin's version.

`<server data directory>/plugins/configurations/Jellyfin.Plugin.Stats.xml`

The settings saved from the plugin's configuration page. This one sits outside
the data folder, in the directory the server keeps every plugin's configuration
in. Both paths come from the server:

    git grep -n "PluginsPath =>\|PluginConfigurationsPath =>" Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs
    58:        public string PluginsPath => Path.Combine(ProgramDataPath, "plugins");
    61:        public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");

run in the `jellyfin/jellyfin` tree at `v10.11.11`. The `v12.0-rc4` tag carries
the same two lines.

The server data directory is the one holding `config`, `data`, `log` and
`plugins`. Where it is depends on how the server was installed, and the server's
own dashboard reports it under the server paths it lists.

## What is inside the data folder

One file.

`plays.db`

The store, a SQLite database holding one row per finished play. The name is the
plugin's own and does not change with the version or with the server.

Nothing else belongs there. SQLite writes a journal beside its database while a
write transaction is open, and that file exists only for the length of the
transaction; the store runs on the default rollback journal, so there is no
write-ahead pair to back up alongside the database. An export writes to a
destination its caller opened and creates no file of its own, so a failed
export leaves nothing behind either.

`DataFolderLayoutTests` is what keeps this section true rather than merely
written. It runs a whole play through the plugin over a temporary server data
directory and lists every file and directory under it before and after, so a
second file appearing here is a red test and not a paragraph somebody has to
remember to update.

## What the retention sweep deletes, and when

`plays.db` also loses things while the plugin is installed, and the retention
sweep is what takes them. It is a scheduled task, it runs daily at three in the
morning by the server's own trigger, and an administrator can start it by hand
from the scheduled tasks page. It answers two windows off one reading of the
clock, both read at the run rather than held from start-up:

    grep -n 'AddDays(-configuration' Jellyfin.Plugin.Stats/ScheduledTasks/RetentionSweepTask.cs
    117:        var cutoff = now.AddDays(-configuration.PlayRowRetentionDays);
    118:        var aggregateCutoff = now.AddDays(-configuration.DailyAggregateRetentionDays);

A play row that started before the first cutoff is deleted: which account
played which item, when it started and stopped, how much was watched, the
client and device it played on, and whether the server transcoded and why.

A daily aggregate keyed to a day before the second cutoff is deleted: one day's
totals per account, item type and client - how many plays, how long, how many
finished, and how the server delivered them. The day is a day in the zone the
store states its rollups were counted in, not the machine's, so the boundary
does not move with where the server is.

The aggregates go first, and the run then reclaims the space both deletions
freed, which is why a store file that found nothing to delete does not shrink.
Neither deletion is a display filter and neither can be undone: the plugin
keeps no second copy, and it takes no export of its own. Where the aggregate
window is the shorter of the two, a deleted day can be folded again from rows
that are still in the file; where it is the longer, which is the shipped
arrangement, the aggregate was the only remaining record of that day and its
deletion is terminal. `docs/configuration.md` is where the two numbers are set
and where that difference is argued.

## Uninstalling

Removing the plugin deletes both of the paths above. The server itself deletes
only the folder the plugin was installed into, which is a third place and holds
the assembly rather than any recorded playback, so the plugin deletes its own
two as it is being removed.

A deletion that fails is written to the server log with the path in the message,
and the path is then still there to be deleted by hand. That is the case worth
knowing about: the hook runs while the server is uninstalling, and a file held
open by something else cannot be removed at that moment.

One case is deliberately left to the server. Where the plugin was installed by
hand into a folder named `Jellyfin.Plugin.Stats`, the data folder and the
installation folder are the same directory, and deleting it from inside the
running plugin would take the assembly out from under the uninstall that is in
progress. The server deletes that folder itself immediately afterwards, so the
data still goes; the log says which of the two happened.

## What this document does not cover

What the plugin records, who can read it and what is refused on purpose are in
`docs/what-is-stored.md`. How long a row and how long a daily aggregate are
kept before the sweep above deletes them are two settings, and
`docs/configuration.md` is the reference for both. This document is about the
two paths, what leaves them on a schedule and the removal, and nothing in it
should be read as a statement about what the contents mean.
