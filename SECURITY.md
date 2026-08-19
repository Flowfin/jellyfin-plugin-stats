# Security policy

## What this plugin is, so a report can be aimed

This is a Jellyfin plugin. It runs inside somebody else's media server, in that
server's process, with whatever that process can reach. It subscribes to the
server's playback events and writes one row per finished play into a SQLite file
in its own data folder: which user played which item, when, from which client
and device, how much of it was watched, and whether the server had to transcode.

So the interesting failure here is a disclosure one. What this plugin holds is
per-user viewing history, and the question the design turns on is who can see
whose numbers. That is what I would ask of this tree first.

The state of the tree changes how you read the rest of this file. Plays are
captured and stored today, nothing reads them back, and the plugin serves no
endpoint at all:

    gh api 'search/code?q=repo:Flowfin/jellyfin-plugin-stats+ControllerBase' \
      --jq '.total_count, (.items[].path)'
    3
    docs/no-custom-query-surface.md
    docs/transcode-reasons.md
    docs/what-is-stored.md

All three hits are prose. The only C# file matching `HttpGet` is
`tools/invariants/near-miss/no-query-from-the-request/SecondSortOrder.cs`, which
lives outside the project directory and is not compiled in.

## Reporting

Report privately, through this repository's advisory form:

https://github.com/Flowfin/jellyfin-plugin-stats/security/advisories/new

That channel answers today:

    gh api repos/Flowfin/jellyfin-plugin-stats/private-vulnerability-reporting
    {"enabled":true}

Please do not open a public issue for anything you believe lets one person read
another person's viewing history.

I promise no acknowledgement deadline. A deadline this project cannot keep is
worse than no deadline at all: a reporter told to expect an answer by a given
day and left without one cannot tell whether the report arrived, was read, or
was lost, and has no way to find out. So there is no date here, and there is an
advisory queue I read.

Four things make a report easy to act on. The server line, because 10.11 on
.NET 9 and 12.0 on .NET 10 are both supported and do not behave identically. The
commit, because with no release a finding is against a tree. What the attacker
holds at the start, which decides everything: no account on the server, an
ordinary account, an administrator account, or read access to the server's data
directory. And what they ended up with that they should not have.

Please do not attach a `plays.db` or an archive exported from a live server.
That file is exactly the data this policy exists to protect. The shape of a row
is in `docs/what-is-stored.md`, so send the shape, or rows you invented.

## What I want to hear about

**A value compiled into a page asset.** The server hands a plugin's
configuration page to callers who have not signed in: the action returning page
content carries no authorization attribute and the server configures no fallback
policy, measured against `v10.11.11` in `docs/page-assets.md` and not evaluated
for the 12.0 line. So the bytes in an embedded page are bytes a stranger can
fetch. A user name, an identifier, a total, a stored setting or a token inside a
shipped page asset is a vulnerability here, whatever put it there. So is a page
loading a script, a style or a font from another host: that is unreviewed code
in a signed-in administrator's dashboard, over a request that tells its host
which server opened the page.

**Anything that reads a row back.** When the first reporting endpoint lands, it
has to answer whether a signed-in user can obtain rows belonging to a different
user, and whether a caller with no account can obtain any row at all. Until
then, a report about an endpoint has to name the branch it is on.

**A row written that the gate should have refused.** `CaptureGate.Records` is
the one place deciding whether a play is recorded, and three of the four
controls an operator has run through it: `CaptureEnabled`, `ExcludedUserIds`
and `ExcludedItemTypes`. A row reaching the store with capture switched off, or
for a user in `ExcludedUserIds`, or for an excluded item type, is a finding.
`PlayRowRetentionDays` is the fourth control and it is not in the gate at all;
it is read by the retention sweep after the row is already written, so it
belongs under the heading below rather than this one.

**A deletion that does not delete.** Deleting a user, the daily retention sweep,
the daily sweep for accounts the server no longer has, and uninstalling the
plugin are the four routes by which history goes away. Rows surviving any of
them, or freed space still holding readable rows afterwards, is a finding.

The uninstall is the route worth aiming at, because it is the one where a
deletion that did not delete leaves an unencrypted per-user history on a disk
nobody is watching any more. `Plugin.OnUninstalling` calls
`PluginDataRemoval.Remove`, which deletes the data folder holding `plays.db`
and the configuration file that sits outside it. Two branches end with the data
still on disk and neither of them stops the uninstall: a deletion that throws is
caught and named on the server log and nowhere else, and a data folder that is
the folder the assembly was loaded from is left for the server's own removal to
take. Readable rows after an uninstall that reported itself finished is a
finding.

**Detail reaching a place it was kept out of.** The network endpoint a session
came from, the client's user agent and the item's path on disk are absent from
the schema on purpose, and any of them in a stored row turns playback statistics
into a record of where somebody was. The server log is the other such place: it
is read by more people than the database is and outlives every retention setting
here, so a user name, an item, series, episode or album title, or a consent
state in a line this plugin writes is a finding, at any level.

**A statement whose shape depends on its input.** Every SQL statement in the
store is a constant with bound parameters. One assembled from strings, or a
caller-supplied column, sort or query fragment reaching SQLite, is a finding.

**The archive reader.** `PlayArchive.Import` parses JSON Lines this process did
not write, and it is the only parser here reading outside input. Nothing in the
running plugin calls it yet, only the test suite. A crash, unbounded memory, or
a row landing in the store that the format should have refused is worth sending
even so.

## What is not a vulnerability here

**The configuration page being fetchable without signing in.** That is the
server's routing rather than this plugin's, and it is why the page carries no
name, no total, no stored setting and no token. The one identifier compiled into
it is the plugin's own, which `build.yaml` publishes to a catalogue anyway, so
that is not the finding either. If you think the routing should change, that
belongs with `jellyfin/jellyfin`; here the finding would be somebody else's
value inside the page.

**Whoever can read the server's disk can read the history.** `plays.db` is an
unencrypted SQLite file at a documented path, so a backup of the server is a
backup of everybody's viewing history. `docs/plugin-data.md` gives the path and
`docs/what-is-stored.md` names the readers, rather than either of them hiding
it, and filesystem access to the host is outside what a plugin can defend
against. An operator is not the attacker in this model.

**Missing privacy features.** There is no consent record, a user cannot read,
export or delete their own history, and an administrator cannot read anybody's
through the plugin either. `docs/what-is-stored.md` states each of the three as
an absence: the consent record against issue #42, the per-user read, export and
delete against issues #43 and #46, and the administrator's with no issue behind
it yet. Those are things this plugin does not have yet.

**Findings in Jellyfin itself, or in another plugin sharing the server.** I can
fix only what is in this tree. Report those to the project that owns them.

**A package claiming to be this plugin.** There is no release:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq 'length'
    0

so anything offering an installable `Jellyfin.Plugin.Stats.dll` as this plugin
today did not come from here. Tell me, but as an impersonation rather than as a
bug in this code.

**A dependency advisory with no path from this plugin.** A scanner naming a
package in the graph is a starting point. Show which call in this tree reaches
it and what an attacker gets, or it is a dependency bump. The graph is pinned in
`packages.lock.json`, and a locked restore refuses anything else.

## Versions, and what happens after you report

With no release, the version that matters is the default branch, `master`. When
releases exist there will be one package per supported server line, and a fix
will land on `master` and go out in the next package on each.

I read the advisory, say what I found, and tell you if I disagree and why. If it
is real I fix it on `master`, publish the advisory, and credit you by whatever
name you give me unless you would rather not be named. No date is attached to
any of that, for the reason above.
