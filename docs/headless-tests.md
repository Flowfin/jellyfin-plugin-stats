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
- a privileged port
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

**Producing a real transcode to get real transcoding data.** It needs an
installed encoder and hardware that differs from runner to runner. Replaced by
fixtures carrying recorded transcoding fields, fed through a fake session.

**Proving the plugin talks to no external service by watching the network.** It
needs a network namespace and root. Replaced by an invariant rule that refuses
an outbound HTTP client in the plugin project at all, which is a stronger
statement than one observed quiet run. That rule is not written yet; the rule
file is where it will be, and until it is there, this paragraph describes an
intention rather than a mechanism.

## What holds it

Four rules in `tools/invariants/rules`, each with a near miss beside it that
fires it:

- `no-test-that-drives-a-browser`
- `no-test-that-needs-a-display`
- `no-test-that-asks-for-elevation`
- `no-test-that-touches-a-certificate-store`

They run on every pull request as `Enforce greppable invariants`, over every
tracked file rather than over the test project alone, because the way each of
these arrives is usually a line in a workflow rather than a line in a test.

That reach is why this document names the tools by description and not by their
literal names: the lint reads this file too, and a policy that quoted the strings
it refuses would be refused by its own rules. The literals live in the rule file,
which is the one directory the scan does not read.

## What is left to a person

The pages are read by hand before a release, and that reading is recorded in the
release checklist as a reading. It is not a test, it does not gate anything by
itself, and calling it one would be the first step in believing the pages are
covered.

## What has not been shown

`dotnet test` has not been run on a runner with no display and no administrator,
because this repository has no test project yet. That is issue #20, and until it
lands the policy above is held by the four rules and by nothing else.
