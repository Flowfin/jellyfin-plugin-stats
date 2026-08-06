"""Judge one pull request's hygiene from JSON the previous step fetched.

Every input is read from a file on disk. Nothing here is interpolated into a
shell command and nothing is executed, because a pull request body, title and
filename are attacker-controlled text on a public repository.

The failing tier refuses a change that names no issue. The warning tier
annotates and never refuses, because how large a change may be and whether it
owes a test are judgements this cannot make.
"""

import json
import os
import re
import sys

ISSUE_REFERENCE = re.compile(r"#[0-9]+")

# The inherited review cap. Above it the change is annotated, never refused:
# a diff can be large and still be one readable thing.
LARGE_CHANGE_LINES = 400

PLUGIN_CODE = re.compile(r"^Jellyfin\.Plugin\.[A-Za-z0-9.]+/.*\.cs$")
TEST_CODE = re.compile(r"(^tests/|\.Tests/|\.Tests\.)")


def read_json(path):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def read_json_lines(path):
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def annotate(level, message):
    # The runner reads these off stdout. Newlines would end the annotation
    # early, so they are replaced rather than trusted.
    print("::{0}::{1}".format(level, message.replace("\n", " ")))


def main():
    repo = os.environ["PR_REPO"]
    pull = read_json("pr.json")
    commits = read_json_lines("commits.json")
    files = read_json_lines("files.json")

    head = pull.get("head") or {}
    head_repo = head.get("repo") or {}
    from_outside = head_repo.get("full_name") != repo

    failures = []

    body = pull.get("body") or ""
    if not ISSUE_REFERENCE.search(body):
        failures.append(
            "The pull request body names no issue. Every change in this plan "
            "starts as one; write the number in the body."
        )

    for commit in commits:
        if commit.get("parents", 1) > 1:
            # A merge commit carries no topic of its own, so it owes no
            # reference. The sign-off check skips them for the same reason.
            continue
        message = commit.get("message") or ""
        if not ISSUE_REFERENCE.search(message):
            failures.append(
                "Commit {0} names no issue in its message.".format(commit.get("sha", "")[:8])
            )

    changed_lines = sum(int(f.get("changes") or 0) for f in files)
    if changed_lines > LARGE_CHANGE_LINES:
        annotate(
            "warning",
            "This change is {0} lines against a {1} line guide. That is a size to "
            "argue about in the body, not a refusal.".format(changed_lines, LARGE_CHANGE_LINES),
        )

    names = [f.get("filename") or "" for f in files]
    touches_plugin = any(PLUGIN_CODE.match(name) for name in names)
    touches_tests = any(TEST_CODE.search(name) for name in names)
    if touches_plugin and not touches_tests:
        annotate(
            "warning",
            "Plugin code changed and nothing under a test project changed with it. "
            "There is no test project in this tree yet; that is issue #20.",
        )

    if from_outside:
        # A fork cannot be asked to follow a convention it cannot read, and the
        # token here is read-only on a fork run anyway. Say why rather than
        # passing silently.
        for failure in failures:
            annotate("notice", "Not refused, author is outside this repository: " + failure)
        annotate(
            "notice",
            "The refusing tier was skipped because the head is {0} rather than {1}.".format(
                head_repo.get("full_name") or "a deleted fork", repo
            ),
        )
        return 0

    for failure in failures:
        annotate("error", failure)

    if failures:
        return 1

    print("Body and every non-merge commit name an issue.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
