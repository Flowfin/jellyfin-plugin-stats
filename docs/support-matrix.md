# Which server this plugin runs on

Two server lines are supported and nothing else is. They run on different
frameworks, so there is one artifact per line and a server is offered the one
that matches the version it reports.

| server line | framework | oldest server the artifact is built against | the SQLite that server ships | targetAbi the package declares | newest released plugin version |
| --- | --- | --- | --- | --- | --- |
| 10.11 | net9.0 | 10.11.0 | 9.0.10 | 10.11.0.0 | 0.1.1.0 |
| 12.0 | net10.0 | 12.0.0-rc1 | 10.0.9 | 12.0.0.0 | no release yet |

Every cell in that table is checked against the value the build uses, by
`SupportMatrixTests` in the suite. A floor bumped in `Directory.Build.props`, a
framework added to or removed from the plugin, an abi changed in `build.yaml` or
in the packaging workflow, or a release cut without this table moving, each
turns this document red rather than leaving it quietly wrong. What each cell is
compared against is written in that file next to the comparison.

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

The newest tag of the 10.11 line is the version the row names, and what that
tag is is derived rather than written here, because a number in this paragraph
drifts against the releases it describes:

    gh api repos/Flowfin/jellyfin-plugin-stats/releases --jq '.[].tag_name'

The row and the newest heading in `CHANGELOG.md` move in the change that raises
`version`, and the tag is pushed on the merge of that change. So between the two
there is a window in which this row names a version the command above does not
print yet, and a reader who finds one meeting the other should read the window
rather than a defect. What the window may not become is permanent: a heading
that never got its tag is a release that does not exist.

The 12.0 line has none. Its stream starts at `1.0.0.0`, which is the decision
issue #133 recorded, and no artifact for that line has been tagged.

The cell is the newest release of the line and not a list, so a line with
several releases behind it names one number, and a reader wanting the rest
opens `CHANGELOG.md`, where every release has a heading.

What the check compares the cell against is that heading. The suite reads no
network and no git, and the checkout the test workflow runs in carries no tags,
so the newest tag is represented in the tree by the newest `## X.Y.Z.W`
heading in `CHANGELOG.md` whose leading number is the line's stream: `0` for
10.11, `1` for 12.0. A line with no such heading says `no release yet`. Whether
that heading and the tag agree is the one part no test here reads, and it is
read by the command above; a release cut without a heading is refused
before that by `docs/RELEASING.md`'s own step of raising the version and the
changelog in one change.

`build.yaml` carries the version the next tag will have, and the sequence
issue #133 settled is raising it first and tagging second, because a release
deleted to correct its number burns that tag permanently. So the file is
allowed above the newest heading and refused below it:

    grep -n '^version:' build.yaml
    10:version: "0.1.1.0"

That is equal today, because the raise and the heading landed in one change,
which is what the release step asks for. A raise that lands on its own leaves
the file above the newest heading, and this table does not move for it: nothing
has been released by a raise, and the row moves with the heading rather than
with the number.
