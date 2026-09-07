# Changelog

## Where the version lives

`build.yaml` is the one tracked file that writes the version as a literal. It is
the file the plugin package carries to a server and to a catalogue, so it is the
one a wrong number is most expensive in. `Directory.Build.props` reads the number
back out of it, and the assembly, file and package versions all come from there:

    dotnet build Jellyfin.Plugin.Stats/Jellyfin.Plugin.Stats.csproj \
      -getProperty:Version -getProperty:AssemblyVersion -getProperty:FileVersion

Bump the version in `build.yaml` and add the entry here in the same change.
Nothing in the tree refuses a bump that leaves this file untouched. That is a
habit, not a gate, and it will stay one until a check is written for it.

The newest `## X.Y.Z.W` heading below is the newest release, and
`SupportMatrixTests` reads it as such: the plugin versions column of
`docs/support-matrix.md` has to name it, and `build.yaml` may not carry a
number below it. A release is therefore a heading here before it is a tag,
and the number in `build.yaml` moves first, as issue #133 settled.

## Unreleased

What has landed on `master` since `0.1.1.0-stable` and is in no release yet.
The change that raises the version moves these under its heading.

Nothing yet.

## 0.1.1.0

Cut as `0.1.1.0-stable` on the 10.11 line only, from the merge of the change
that raised `version` in `build.yaml`. This heading is written before that tag
exists, which is the sequence the section above describes and issue #133
settled. The 12.0 line still has no release: its stream starts at `1.0.0.0` and
waits for a 12.0.0 that is not a release candidate.

What this release is for: 0.1.0.0 bound a SQLite stack no 10.11.0 server
carries, so on the floor server of its own line it loaded and then recorded
nothing. That is repaired here, and the floor is now read on every dispatch of
the install-route reading rather than only the newest patch of the line.

What a server installs that 0.1.0.0 did not: four aggregate reports an
administrator reads over the server's API, two further routes a signed-in user
opens about themselves, and the day-by-day rollups both are read from. Still no
statistics page in the dashboard: the three views are built, embedded and
tested, and they are not declared, for the reason the entry below gives.

- The SQLite stack the plugin binds is the one the floor server of each line
  ships: `Microsoft.Data.Sqlite 9.0.10` on the 10.11 line and `10.0.9` on the
  12.0 line. 0.1.0.0 bound 9.0.11.0, which a 10.11.0 server does not carry, so
  on that server it loaded and could not open its store. Issue #330.

- A save this plugin refuses no longer replaces the running configuration. The
  server assigns the incoming object before it asks for the save, so a refused
  value left the process running on that object's defaults until a restart,
  and the retention sweep read them. The value guard is now asked before
  anything is assigned. Issue #331.

- The plugin declares the settings page to the server and nothing else. The
  three views built since 0.1.0.0 are embedded and tested, and they are not
  declared until they render: the dashboard translates every plugin page
  before it inserts it and that translation breaks the modules' template
  literals, so a declared view was a blank page. Issue #335 serves the code
  from the plugin and puts the declarations back. Issue #332.

- Four aggregate reports for an administrator, over the server's API: the most
  watched titles, the breakdown by client or by device, usage over a range one
  row per day, and the server's year. Every choice a request makes goes
  through a closed set, and a row fewer than two accounts stand behind is
  withheld or folded into one group rather than shown.

- A signed-in user reads their own figures over three windows and the years
  the store holds for them, on two more routes about themselves. Both top
  lists an account reads about itself apply one visibility rule: an item the
  account may no longer see is not named.

- Day-by-day rollups are folded from the play rows as they are written, are
  rebuilt from the rows when a corrective deletion moves them, are what the
  year and the self figures are read from, and go when their own retention
  window says so. The two caps on the settings page bind the aggregate reports.

- The daily sweep for accounts the server no longer has takes their consent
  record too, not only their rows.

- Three views assembled from the tracked drawing modules, usage over time,
  your year and your statistics, and three further modules that are on no
  page yet. Embedded and not declared, for the reason above.

- The bill of materials is attached to the release it describes, from the
  next tag onward, and a reading follows the documented install route on a
  fresh server.

## 0.1.0.0

Released 2026-08-24 from `083fdbdb`, the merge of #240, on the 10.11 line
only. The 12.0 line has no release: its stream starts at `1.0.0.0` and waits
for a 12.0.0 that is not a release candidate.

What a server installs: one row per finished play, written into a SQLite file
of the plugin's own, with which user played which item, when, from which
client and device, how much of it was watched and whether the server had to
transcode; a settings page for what is captured and how long it is kept; and
three routes a signed-in user opens about themselves, their year, their
consent, and the deletion of their own history. Nobody reads another person's
rows on any of them.

What it does not do: show a statistic anywhere. One page is declared, the
settings page, and no report route exists.

What is wrong with it: the assembly binds `Microsoft.Data.Sqlite 9.0.11.0` and
the archive ships nothing but the assembly, so the stack comes from the
server. A 10.11.0 server carries 9.0.10.0, and there the plugin loads and
then cannot open its store, so it records nothing. Measured on issue #329 and
repaired under #330 for the next release.

- The 10.11 line's version stream starts at `0.1.0.0`, which `build.yaml`
  carries in place of `0.0.0.0`. The 12.0 line's stream starts at `1.0.0.0`, so
  the leading number says which server line a release is for rather than how
  settled the plugin is, and `README.md` says so where a reader meets the
  number. The number is raised before a tag exists because a release deleted
  to correct its number burns that tag permanently.

- The version is written once, in `build.yaml`, and read from there by the
  build. It was previously a literal in two files that disagreed with each
  other. The number the compiled assembly already carried is the one kept,
  because no release had been made and the higher number in the package
  manifest advertised one that did not exist.

- The plugin builds for both supported server lines: 10.11 on .NET 9 and 12.0
  on .NET 10, each against the Jellyfin packages that line publishes. It
  previously targeted one framework, compiled against a 10.9 server and
  declared a target abi no supported server has. Packaging produces one
  package per line and reads the abi back out of each zip, so a package
  carrying the wrong line's abi fails the run instead of reaching a server.
