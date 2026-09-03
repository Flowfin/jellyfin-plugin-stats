# How the tests are allowed to run

Every test in this repository runs on a plain runner: no screen, no
administrator, and nothing left behind on the machine afterwards. That is a
property of the plan rather than something discovered later, so it is written
here before the tests exist, and the rules that hold it are in
`tools/invariants/rules` rather than in this file alone.

## What no test may need

- a graphical display, or a browser driven by a test
- elevated rights, in any spelling
- the machine's certificate store, or a development certificate trusted into it
- a listening port, whether or not binding it needs rights
- an installed media encoder
- a running Jellyfin server
- a network call to anything outside the run

## Four things this plan would otherwise want, refused by name

Each is refused with the replacement that carries the same weight, so the
refusal is a decision about how the property is proved rather than a decision to
stop proving it.

**A browser driving the dashboard pages end to end.** It needs a display or a
downloaded browser and a running server. Replaced by keeping every page's data
shaping in a plain module with no document access, unit testing that module
directly, and testing the endpoints in process. What is left over is a short
manual reading in the release checklist, recorded as a reading and never
described as a test.

**Installing the built plugin into a real server to prove it loads.** It needs a
server install and a service restart. Replaced by a test that loads the built
assembly, finds the plugin type, and asserts its constructor shape and its
identifier, together with the floor builds against the oldest supported server
of each line.

That refusal stands for every TEST and it is not the whole of what this
document now says: one dispatched READING does install the packaged plugin into
a real server, for a question the replacement above cannot reach. It is
declared below rather than exempted here, and a reader who takes this paragraph
for the last word on the subject would be reading it as it stood before that
section existed.

**Producing a real transcode to get real transcoding data.** It needs an
installed encoder and hardware that differs from runner to runner. Replaced by
fixtures carrying recorded transcoding fields, fed through a fake session.

**Proving the plugin talks to no external service by watching the network.** It
needs a network namespace and root. Replaced by an invariant rule that refuses
an outbound HTTP client at all, which is a stronger statement than one observed
quiet run because it holds for the runs nobody watched. That rule is
`no-outbound-http-client` and it reads every tracked C# file, so it is wider than
the plugin project: a client added to the suite is refused on the same line as
one added to the plugin.

## What holds it

Six rules in `tools/invariants/rules`, each with a near miss beside it that
fires it:

- `no-test-that-drives-a-browser`
- `no-test-that-needs-a-display`
- `no-test-that-asks-for-elevation`
- `no-test-that-touches-a-certificate-store`
- `no-test-that-binds-a-port`
- `no-outbound-http-client`

They run on every pull request as `Enforce greppable invariants`, over every
tracked file rather than over the test project alone, because the way each of
these arrives is usually a line in a workflow rather than a line in a test.

The port rule refuses opening a port and the host that would serve over one; it
does not refuse routing a request to a controller in process, which is what the
endpoint tests do instead. That route exists now, and this paragraph said it did
not: a request object is handed to a pipeline built out of the framework the
server runs on, so the routing, the authentication, the authorization filters,
the model binding and the result execution are the real ones and nothing is
opened. The plugin has one endpoint and it is driven that way, as the caller who
owns the rows, as two who do not, and as nobody at all.

What that route does not prove is worth keeping in front of a reader. It stands
where a server stands rather than being one, so what it shows is which caller an
action admits and what the action then answers, and never that this plugin's
assembly is loaded, that its controller is found, or that the server's own
authentication produces the caller these tests hand it. Those are statements
about a server and are read by hand before a release, in the checklist, as a
reading.

That reach is why this document names the tools by description and not by their
literal names: the lint reads this file too, and a policy that quoted the strings
it refuses would be refused by its own rules. The literals live in the rule file,
which is the one directory the scan does not read.

## A reading that needs a server, and why it is not a test

Two jobs in this repository start a real Jellyfin server, publish a port on
the runner they are on, put this plugin into it and ask it a question. Both of
those are on the list above, and the second job adds a third. This section is
the departure, written where the rule is rather than in the files that take it.

**What it is.** A dispatched job, `Reading`, driving
`tools/reading/settings-page-reading.py`. It answers one question that no
in-process route here can reach: two properties of the plugin configuration
have a getter and no setter and are kept out of the stored file, the settings
page draws both, and nothing had observed that such a property arrives in what
that page receives. What answers it is the server's own serialisation of its
own configuration object, and the server is the thing under the question, so
standing where a server stands is not available here. Issue #31 carries the
argument.

**The second.** A dispatched job, `Reading of the install route`, driving
`tools/reading/install-route-reading.py`. It answers issue #81's second
condition: whether a fresh server that follows the route `README.md` gives a
person - add one address, install from the catalogue it serves - ends up with
this plugin loaded. Nothing from the checkout is installed. The server is
handed the address and fetches whatever it serves, hashes the archive against
the checksum the catalogue carries, and refuses the install when they
disagree, so what is read is the published route and not the tree. That is
also a third thing from the list above, a network call to something outside
the run, and it is the question rather than a shortcut to it: the route a
user follows goes over the network or it is not the route. The two readings
share what every reading does first, in `tools/reading/jellyfin_server.py`,
and nothing else.

**Why it is not a test, in the sense the list above uses.** No trigger runs it,
no ruleset names its context, and nothing merges or fails on it. Its output is
text somebody pastes into an issue, and the register that holds it is the issue
and not a green check. That is the same shape as the manual reading in the
release checklist, one step further along: the runner takes it instead of a
person, and it is still a reading.

**What the distinction does not buy, and this is the half to read carefully.**
It buys nothing for any test. A test that reached for a server or a port would
be refused by this document exactly as before, and the replacements named above
are still the answer for every one of them. A reading is also the weaker
artefact: it proves the run it was taken in and nothing about the runs nobody
dispatched, which is the opposite of what an invariant rule buys and is why
this route is not the one anything else here moves to.

**The costs, stated.** Either job can rot between dispatches with nothing
saying so. Both read an image from outside the run, pinned by digest so that at
least the same bytes answer twice, and the second reads a manifest that is
pinned to nothing, because a manifest that moved is exactly what it exists to
notice. And the port, published on the loopback address, is a resource shared
with everything else on that runner - the reason the rule above gives for
refusing one does not stop applying because this is a reading, it is paid.

## What is left to a person

The pages are read by hand before a release, and that reading is recorded in the
release checklist as a reading. It is not a test, it does not gate anything by
itself, and calling it one would be the first step in believing the pages are
covered.

## What has been run, and what is still not shown

The suite exists now, and `dotnet test` runs on every pull request as the
required `call / test` context, on a hosted `ubuntu-24.04` image. The run that
came with this paragraph is named in that pull request's body. The job is five
steps, a checkout, a .NET setup, a restore, a build and the test command, and
none of them opens a display, asks for elevation or writes outside the
workspace.

What that does not show is a runner where those things are unavailable. Whether
the hosted image has a display, and whether it would grant elevation to a test
that asked, have not been measured here. So a green run is evidence that the
suite does not need either, and it is not evidence that the runner would refuse
them. The policy is held by the rules and by what the job does, and not by the
runner turning anything down.
