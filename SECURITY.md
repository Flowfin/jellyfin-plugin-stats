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
captured and stored, and ten actions on five controllers read them back:

    git grep -n '\[Http\(Get\|Post\|Put\|Delete\)' -- 'Jellyfin.Plugin.Stats/Api/*.cs'
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:162:    [HttpGet("Top")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:254:    [HttpGet("Breakdown")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:341:    [HttpGet("Usage")]
    Jellyfin.Plugin.Stats/Api/AggregateReportsController.cs:428:    [HttpGet("Year/{year:int}")]
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:67:    [HttpGet]
    Jellyfin.Plugin.Stats/Api/YourConsentController.cs:118:    [HttpPut]
    Jellyfin.Plugin.Stats/Api/YourHistoryController.cs:108:    [HttpDelete]
    Jellyfin.Plugin.Stats/Api/YourStatisticsController.cs:128:    [HttpGet("{window}")]
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:161:    [HttpGet]
    Jellyfin.Plugin.Stats/Api/YourYearController.cs:225:    [HttpGet("{year:int}")]

Six of them are about one account and answer only that account, whoever asks;
four are server-wide, answer an administrator only, and name nobody unless that
person recorded their consent. `AuthorizationMatrixTests` holds every row of
that table, and the release a server installs today carries three of the five
controllers, consent, history and year, and neither the aggregate reports nor
the statistics route, which is what `CHANGELOG.md` says under `0.1.0.0`.

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
version, if the plugin was installed from a release, or the commit, if it was
built from `master`, because the two differ and a finding is against one of
them. What the attacker holds at the start, which decides everything: no account
on the server, an ordinary account, an administrator account, or read access to
the server's data directory. And what they ended up with that they should not
have.

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
which server opened the page. One page is declared today and four are embedded;
the three views are withheld until they render, and every one of the four is
public in the sense above.

**A row reaching the wrong caller.** The rule every route holds is that a
signed-in account sees its own rows and nobody else's, and that an administrator
is refused on a personal route by the same line as everybody else. A caller
obtaining another account's rows, on any route and whatever they send, is the
finding this plugin exists to prevent. So is a caller with no account obtaining
any row at all, and so is a server-wide answer naming an account that holds no
consent record, because consent is the only thing that puts a name on one.

**A row written that the gate should have refused.** `CaptureGate.Records` is
the one place deciding whether a play is recorded, and three of the four
controls an operator has run through it: `CaptureEnabled`, `ExcludedUserIds`
and `ExcludedItemTypes`. A row reaching the store with capture switched off, or
for a user in `ExcludedUserIds`, or for an excluded item type, is a finding.
`PlayRowRetentionDays` is the fourth control and it is not in the gate at all;
it is read by the retention sweep after the row is already written, so it
belongs under the heading below rather than this one.

**A deletion that does not delete.** Deleting a user, the daily retention sweep,
the daily sweep for accounts the server no longer has, a user deleting their own
history, and uninstalling the plugin are the five routes by which history goes
away. Rows surviving any of them, or freed space still holding readable rows
afterwards, is a finding. A consent record surviving its account is one too.

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
`docs/no-custom-query-surface.md` says what the ten actions take instead.

**The archive reader.** `PlayArchive.Import` parses JSON Lines this process did
not write, and it is the only parser here reading outside input. Nothing in the
running plugin calls it, only the test suite:

    git grep -n 'PlayArchive\.Import' -- 'Jellyfin.Plugin.Stats/*.cs' ; echo "exit=$?"
    exit=1

A crash, unbounded memory, or a row landing in the store that the format should
have refused is worth sending even so.

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

**Absent privacy features.** A user cannot stop their own plays being recorded,
and cannot export their own history; `docs/what-is-stored.md` states both as
absences under "What this plugin does not have yet". An administrator cannot
read anybody's detail through this plugin, and that one is a rule rather than
an absence: an elevated route to one person's rows is not part of the plan and
would be a decision of its own. None of the three is a finding. An
administrator reaching a person's rows through this plugin would be.

**Findings in Jellyfin itself, or in another plugin sharing the server.** I can
fix only what is in this tree. Report those to the project that owns them.

**A package claiming to be this plugin.** One release exists, and it carries
its own checksums:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq '.[] | .tag_name, (.assets[].name)'
    0.1.0.0-stable
    playback-statistics_0.1.0.0.md5
    playback-statistics_0.1.0.0.sha256
    playback-statistics_0.1.0.0.zip
    playback-statistics_0.1.0.0.zip.meta.json

An archive offered as this plugin whose checksum is not the one on that
release, or under a version that release list does not carry, did not come from
here. Tell me, but as an impersonation rather than as a bug in this code.

**A dependency advisory with no path from this plugin.** A scanner naming a
package in the graph is a starting point. Show which call in this tree reaches
it and what an attacker gets, or it is a dependency bump. The graph is pinned in
`packages.lock.json`, and a locked restore refuses anything else.

## Versions, and what happens after you report

There is one package per supported server line, and one release so far, on the
10.11 line: `0.1.0.0`. The 12.0 line has none. `master` is ahead of that
release, so a finding is against the version installed or against a commit,
and the report should say which. A fix lands on `master` and goes out in the
next package on each line that has one.

I read the advisory, say what I found, and tell you if I disagree and why. If it
is real I fix it on `master`, publish the advisory, and credit you by whatever
name you give me unless you would rather not be named. No date is attached to
any of that, for the reason above.
