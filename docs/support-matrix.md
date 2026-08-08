# Which server this plugin runs on

Two server lines are supported and nothing else is. They run on different
frameworks, so there is one artifact per line and a server is offered the one
that matches the version it reports.

| server line | framework | oldest server the artifact is built against | targetAbi the package declares | plugin versions |
| --- | --- | --- | --- | --- |
| 10.11 | net9.0 | 10.11.0 | 10.11.0.0 | none released |
| 12.0 | net10.0 | 12.0.0-rc1 | 12.0.0.0 | none released |

Every cell in that table is checked against the value the build uses, by
`SupportMatrixTests` in the suite. A floor bumped in `Directory.Build.props`, a
framework added to or removed from the plugin, an abi changed in `build.yaml` or
in the packaging workflow, or the first release being cut, each turns this
document red rather than leaving it quietly wrong. What each cell is compared
against is written in that file next to the comparison.

## A server outside the table

Unsupported. Not "probably works", not "untested": there is no artifact for it
and no claim is made about it.

A server older than the floor of its line is unsupported for a reason that bites
at install time rather than later. The package declares the abi in the table,
and a server below it is not offered the plugin at all. A server on a line that
is not 10.11 or 12.0 has no artifact here in any case.

## What the floor column means, and what it does not

The floor is the oldest release of a line that the shipped artifact is compiled
against. Compiling against the floor is what makes the whole line safe: a call
added against a later release of a line compiles cleanly and then fails to load
for everybody still on the floor. Two jobs in `.github/workflows/build.yaml`
build each line against its floor on every pull request, so the column is a
statement the build re-proves rather than a note somebody kept up to date.

The 12.0 line has published no stable release, so its floor is a release
candidate. That row moves when there is a release, and it says a candidate
rather than implying otherwise.

The column is true of the artifact and not of the test run. On the 10.11 line
the suite runs against 10.11.11 rather than against the floor, because
`IUserManager` in 10.11.0 declares members 10.11.11 does not and the fake in the
suite does not compile against the older interface. So the plugin is proved to
compile against the floor and the tests are not proved to run against it, and
those are different statements.

## Plugin versions

Nothing has been released:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq 'length'
    0

    grep -n '^version:' build.yaml
    4:version: "0.0.0.0"

Both rows say so, and the check reads the second of those two commands. When a
version ships, the rows stop saying "none released" and the check stops
accepting `0.0.0.0`, so the first release cannot land without this table being
brought with it.
