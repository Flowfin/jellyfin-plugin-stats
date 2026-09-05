# Which server this plugin runs on

Two server lines are supported and nothing else is. They run on different
frameworks, so there is one artifact per line and a server is offered the one
that matches the version it reports.

| server line | framework | oldest server the artifact is built against | the SQLite that server ships | targetAbi the package declares | plugin versions |
| --- | --- | --- | --- | --- | --- |
| 10.11 | net9.0 | 10.11.0 | 9.0.10 | 10.11.0.0 | none released |
| 12.0 | net10.0 | 12.0.0-rc1 | 10.0.9 | 12.0.0.0 | none released |

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

## The SQLite column

The plugin keeps its plays in a SQLite file of its own, and the stack that reads
that file is the server's rather than the plugin's: the archive holds one
assembly and nothing else, so nothing about SQLite is shipped and the version the
assembly binds has to be one the server already loads.

That column is therefore the same statement as the floor beside it, one layer
down. It is the `Microsoft.Data.Sqlite` the floor server of the line carries, read
out of the image:

    cid=$(docker create jellyfin/jellyfin:10.11.0)
    docker cp "$cid:/jellyfin/Microsoft.Data.Sqlite.dll" .
    docker rm "$cid"
    [System.Reflection.AssemblyName]::GetAssemblyName("Microsoft.Data.Sqlite.dll").Version
    9.0.10.0

A strong-named assembly found below the referenced version is not found at all,
so a plugin built against a later patch than the floor server ships loads on
every server of the line except the oldest ones, and there it fails at the moment
the store is first opened rather than at start. That is what 0.1.0.0 did on a
10.11.0 server, and issue #330 is where it was measured and repaired.

Later servers on a line ship later versions - 10.11.11 carries 9.0.11.0, 12.0-rc4
carries 10.0.10.0 - and all of them satisfy a reference to the floor. The column
is the floor and not the newest, for exactly that reason.

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

Both rows say so. `build.yaml` no longer agrees with them by carrying a version
that could not be a release, and this section said it did:

    grep -n '^version:' build.yaml
    10:version: "0.1.0.0"

The number moved before any tag exists, on purpose. Issue #133 settled the
sequence as raising `build.yaml` first and tagging second, because a release
deleted to correct its number burns that tag permanently, so there is a window in
which the file names a version and no release carries it. This is that window.

So the column and the file say two different true things, and the check compares
the file against the number written here rather than against a version that means
nothing has shipped:

    awaiting its first tag: 0.1.0.0

That is the 10.11 line, which is the line `build.yaml` is. The 12.0 line's stream
starts at `1.0.0.0`, and where that number is written when the second artifact is
tagged is the release route in issue #80 rather than this file, so no line here
claims it is written anywhere yet.

When a version ships, the rows stop saying "none released" and this comparison
stops being made at all, so the first release cannot land without this table
being brought with it.
