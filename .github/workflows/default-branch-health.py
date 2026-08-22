"""Say which workflows are red on the default branch, from JSON a previous step fetched.

Several checks in this plan run on a schedule rather than on a pull request, so
nothing draws attention to them when they fail: a weekly scan that failed and a
weekly scan that passed look the same until somebody opens the runs list. This
reads the newest concluded run of every workflow and names the ones that did not
succeed.

The newest run per workflow is what is judged, rather than every run in a
window. A watcher that keeps naming a failure somebody has already repaired is a
watcher nobody reads, and the question this answers is whether the default
branch is red now.

Every input is read from a file on disk. A workflow name is text whoever has
push access wrote, and it is printed rather than executed; nothing here is
interpolated into a shell command.

--self-test judges fixtures held in this file instead of the fetched runs. It is
the first step of the job that carries it, so a change that stops this noticing
a red run reds a pull request rather than being discovered on the day something
breaks.
"""

import json
import os
import sys

# A run that concluded either of these ways is not a failure. skipped is here
# because a workflow whose triggers filtered it out did nothing at all, and a
# watcher that called that red would name most of the tree on most days. Every
# other conclusion the API can report, failure, cancelled, timed_out,
# startup_failure, action_required, neutral and stale, is named.
CONCLUSIONS_THAT_ARE_NOT_RED = ("success", "skipped")

RUNS = "runs.json"


def read_json_lines(path):
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def newest_first(run):
    # The moment a run started decides which is newest, and the run's own
    # identifier settles a tie, because two runs of one workflow can carry the
    # same start moment to the second.
    return (run.get("started") or "", run.get("id") or 0)


def newest_concluded_per_workflow(runs, ignore_path=None):
    """The newest run of each workflow that reached a conclusion.

    A run still going carries no conclusion. It is passed over rather than read
    as a success, so a failure behind it stays visible until something newer
    actually concludes.

    ignore_path drops the workflow this job is part of. Without that the watcher
    latches: its own failed run becomes the newest run of its own workflow, and
    every later run reports that and fails for having reported it, long after
    the thing it was reporting was repaired.
    """
    newest = {}
    for run in runs:
        if not run.get("conclusion"):
            continue
        if ignore_path and run.get("path") == ignore_path:
            continue
        key = run.get("workflow_id")
        current = newest.get(key)
        if current is None or newest_first(run) > newest_first(current):
            newest[key] = run
    return newest


def red_runs(newest):
    red = [
        run
        for run in newest.values()
        if (run.get("conclusion") or "") not in CONCLUSIONS_THAT_ARE_NOT_RED
    ]
    red.sort(key=lambda run: (run.get("name") or "", run.get("id") or 0))
    return red


def describe(run):
    return "{0} concluded {1} on run {2}. {3}".format(
        run.get("name") or "an unnamed workflow",
        run.get("conclusion") or "nothing",
        run.get("number") or run.get("id") or "unknown",
        run.get("url") or "",
    ).strip()


def summarise(lines):
    path = os.environ.get("GITHUB_STEP_SUMMARY")
    if not path:
        return
    with open(path, "a", encoding="utf-8") as handle:
        for line in lines:
            handle.write(line + "\n")


def judge(runs, branch, ignore_path=None):
    """Return the exit code and the lines whoever is reading gets."""
    newest = newest_concluded_per_workflow(runs, ignore_path)
    if not newest:
        # A watcher over an empty population reports nothing and passes, which
        # reads exactly like a branch with nothing wrong with it.
        return 1, [
            "::error::No concluded workflow run was read for {0}, so this job "
            "judged nothing. That is a broken read rather than a green "
            "branch.".format(branch)
        ]

    red = red_runs(newest)
    lines = [
        "Read the newest concluded run of {0} workflow(s) on {1}.".format(len(newest), branch)
    ]
    if not red:
        lines.append("Every one of them succeeded.")
        return 0, lines

    lines.append("{0} of them did not succeed:".format(len(red)))
    for run in red:
        lines.append(
            "::error::{0} is red on {1}: {2}".format(
                run.get("name") or "a workflow", branch, describe(run)
            )
        )
    return 1, lines


SELF = ".github/workflows/default-branch-health.yml"

FIXTURES = [
    (
        "a workflow whose newest concluded run failed is named",
        [
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": "failure",
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2"},
            {"id": 1, "workflow_id": 7, "name": "Weekly scan", "conclusion": "success",
             "started": "2026-08-13T02:00:00Z", "number": 11, "url": "https://example.invalid/1"},
        ],
        1,
        ["Weekly scan is red", "run 12"],
    ),
    (
        "a run that failed on a schedule is named the same as any other",
        [
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": "failure",
             "event": "schedule", "started": "2026-08-20T02:00:00Z", "number": 12,
             "url": "https://example.invalid/2"},
        ],
        1,
        ["Weekly scan is red", "run 12"],
    ),
    (
        "a branch whose newest runs all succeeded is said to be green",
        [
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": "success",
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2"},
            {"id": 3, "workflow_id": 8, "name": "Daily scan", "conclusion": "skipped",
             "started": "2026-08-21T02:00:00Z", "number": 30, "url": "https://example.invalid/3"},
        ],
        0,
        ["Every one of them succeeded."],
    ),
    (
        "a failure a later run repaired is not named again",
        [
            {"id": 1, "workflow_id": 7, "name": "Weekly scan", "conclusion": "failure",
             "started": "2026-08-13T02:00:00Z", "number": 11, "url": "https://example.invalid/1"},
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": "success",
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2"},
        ],
        0,
        ["Every one of them succeeded."],
    ),
    (
        "a run still going does not hide the failure behind it",
        [
            {"id": 1, "workflow_id": 7, "name": "Weekly scan", "conclusion": "failure",
             "started": "2026-08-13T02:00:00Z", "number": 11, "url": "https://example.invalid/1"},
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": None,
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2"},
        ],
        1,
        ["Weekly scan is red", "run 11"],
    ),
    (
        "a run that was cancelled or timed out did not succeed either",
        [
            {"id": 1, "workflow_id": 7, "name": "Weekly scan", "conclusion": "cancelled",
             "started": "2026-08-13T02:00:00Z", "number": 11, "url": "https://example.invalid/1"},
            {"id": 2, "workflow_id": 8, "name": "Daily scan", "conclusion": "timed_out",
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2"},
        ],
        1,
        ["Daily scan is red", "Weekly scan is red"],
    ),
    (
        "a population of nothing is refused rather than read as a green branch",
        [],
        1,
        ["judged nothing"],
    ),
    (
        "this job does not report itself",
        [
            {"id": 1, "workflow_id": 9, "name": "Default branch health", "conclusion": "failure",
             "started": "2026-08-20T02:00:00Z", "number": 4, "url": "https://example.invalid/1",
             "path": SELF},
            {"id": 2, "workflow_id": 7, "name": "Weekly scan", "conclusion": "success",
             "started": "2026-08-20T02:00:00Z", "number": 12, "url": "https://example.invalid/2",
             "path": ".github/workflows/weekly.yml"},
        ],
        0,
        ["Every one of them succeeded."],
    ),
]


def self_test():
    failed = 0
    for name, runs, expected_code, expected_lines in FIXTURES:
        code, lines = judge(runs, "a branch", SELF)
        said = "\n".join(lines)
        wrong = []
        if code != expected_code:
            wrong.append("expected exit {0} and got {1}".format(expected_code, code))
        for expected in expected_lines:
            if expected not in said:
                wrong.append("expected to read {0}".format(expected))
        if wrong:
            failed += 1
            print("FAIL {0}".format(name))
            for line in wrong:
                print("     " + line)
            for line in lines:
                print("     said: " + line)
        else:
            print("ok   {0}".format(name))
    print("{0} fixture(s), {1} failed".format(len(FIXTURES), failed))
    return 1 if failed else 0


def main(argv):
    if "--self-test" in argv:
        return self_test()

    branch = os.environ.get("HEALTH_BRANCH") or "the default branch"
    ignore_path = os.environ.get("HEALTH_SELF") or None
    code, lines = judge(read_json_lines(RUNS), branch, ignore_path)
    for line in lines:
        print(line)
    summarise(lines)
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
