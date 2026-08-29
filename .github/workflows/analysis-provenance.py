"""Refuse a code-scanning analysis this branch stands on that this tree did not produce.

On 2026-08-28 a second configuration uploaded a C# analysis under the category
`scan-codeql.yaml` already writes. It had extracted without a build, it reported
zero results where the analysis before it reported a hundred and twenty-eight,
and fifty-seven alerts closed as `fixed` against a scan that compiled nothing.
No source file changed between the two. Issue #273 records it and #277 is the
gap it left: the arrangement that permitted it is a repository setting, this
tree cannot read it, and nothing compared the analyses at a head against the
configuration that should have produced them.

Two properties, and each one refuses that incident on its own.

**from-another-configuration.** The newest analysis in a category has to carry
this repository's own workflow as its `analysis_key`. A key naming anything else
is a configuration outside this tree deciding what the branch stands on.

**emptied-at-one-commit.** Two analyses of one category at ONE commit, where a
later one reports fewer results than an earlier one. The commit is what makes
this refusable rather than noisy: at one commit the tree is the same tree, so a
count that fell did not fall because anything was repaired. Across commits it
says nothing and is not judged - a genuine repair is exactly a count that fell,
and a rule that refused one would be refusing the work it exists to protect.

THE SECOND WOULD NOT HAVE CAUGHT 2026-08-28, AND THIS IS SAID HERE RATHER THAN
LEFT FOR SOMEBODY TO WORK OUT. The empty upload sat at `021f0b7` and the
hundred-and-twenty-eight it replaced sat at `1b913f4`, so the fall was across
two commits and this property does not look there. What refuses that incident is
the first property, on the key the empty upload carried. The second is a
neighbouring shape that is cheap to hold and is held; it is not the reason this
file exists.

The two are not one statement and neither subsumes the other. A foreign
configuration that happened to report the same count walks through the second; a
configuration in this tree that emptied its own analysis at one commit walks
through the first.

Every input is read from a file on disk. A category and an analysis key are
text the API returned; they are printed rather than executed, and nothing here
is interpolated into a shell command.

--self-test judges fixtures held in this file instead of the fetched analyses.
It runs first in the job that carries it, and on pull requests where the fetch
does not, so a change that stops this refusing reds the pull request that made
it rather than being found the next time somebody empties the security tab.
"""

import json
import os
import sys

ANALYSES = "analyses.json"

# The prefix an analysis key carries when this repository's own workflow
# produced it. The API reports `<path>:<job id>`, so the path is matched with a
# separator after it rather than by equality - a job renamed inside the
# workflow is still this tree's analysis, and a workflow whose path merely
# starts with these characters is not.
THIS_TREE = ".github/workflows/"


def read_json_lines(path):
    rows = []
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            line = line.strip()
            if line:
                rows.append(json.loads(line))
    return rows


def newest_first(analysis):
    # The moment an analysis was created decides which is newest, and its own
    # identifier settles a tie, because two uploads can carry one moment.
    return (analysis.get("created") or "", analysis.get("id") or 0)


def from_another_configuration(analyses):
    """The newest analysis of each category that this tree did not produce."""
    newest = {}
    for analysis in analyses:
        category = analysis.get("category") or ""
        if category not in newest or newest_first(analysis) > newest_first(newest[category]):
            newest[category] = analysis

    refused = []
    for category in sorted(newest):
        analysis = newest[category]
        key = analysis.get("key") or ""
        if not key.startswith(THIS_TREE):
            refused.append(
                "{0}: the newest analysis was produced by {1}, which is not this tree's "
                "workflow".format(category, key or "no named configuration")
            )
    return refused, newest


def emptied_at_one_commit(analyses):
    """Categories where a later analysis of ONE commit reported fewer results."""
    by_pair = {}
    for analysis in analyses:
        pair = (analysis.get("category") or "", analysis.get("sha") or "")
        by_pair.setdefault(pair, []).append(analysis)

    refused = []
    for pair in sorted(by_pair):
        ordered = sorted(by_pair[pair], key=newest_first)
        for earlier, later in zip(ordered, ordered[1:]):
            before = earlier.get("results")
            after = later.get("results")
            if before is None or after is None:
                continue
            if after < before:
                refused.append(
                    "{0}: at {1} an analysis reported {2} result(s) after one that reported "
                    "{3}, and the tree is the same tree at one commit".format(
                        pair[0], pair[1][0:7], after, before
                    )
                )
    return refused


def judge(analyses):
    refused, newest = from_another_configuration(analyses)
    refused = list(refused)
    refused.extend(emptied_at_one_commit(analyses))
    return refused, newest


def describe(newest):
    """What each category stands on, printed whether or not anything is refused."""
    lines = []
    for category in sorted(newest):
        analysis = newest[category]
        lines.append(
            "  {0:<32} {1} result(s) at {2} from {3}".format(
                category,
                analysis.get("results"),
                (analysis.get("sha") or "")[0:7],
                analysis.get("key"),
            )
        )
    return lines


FIXTURES = (
    (
        "every category comes from this tree and no count fell",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "aaaaaaa", "created": "2026-08-29T10:17:46Z", "id": 3},
            {"category": "/language:actions", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 0, "sha": "aaaaaaa", "created": "2026-08-29T10:15:40Z", "id": 1},
            {"category": "/language:javascript-typescript", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 4, "sha": "aaaaaaa", "created": "2026-08-29T10:15:56Z", "id": 2},
        ],
        0,
    ),
    (
        "a foreign configuration emptying one category at one commit is refused twice",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "bbbbbbb", "created": "2026-08-27T13:05:58Z", "id": 1},
            {"category": "/language:csharp", "key": "dynamic/github-code-scanning/codeql:analyze", "results": 0, "sha": "bbbbbbb", "created": "2026-08-28T13:09:38Z", "id": 2},
        ],
        2,
    ),
    (
        "a foreign configuration that reported the same count is still refused",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "bbbbbbb", "created": "2026-08-27T13:05:58Z", "id": 1},
            {"category": "/language:csharp", "key": "dynamic/github-code-scanning/codeql:analyze", "results": 128, "sha": "bbbbbbb", "created": "2026-08-28T13:09:38Z", "id": 2},
        ],
        1,
    ),
    (
        "this tree's own workflow emptying its own category is still refused",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "bbbbbbb", "created": "2026-08-27T13:05:58Z", "id": 1},
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 0, "sha": "bbbbbbb", "created": "2026-08-28T13:09:38Z", "id": 2},
        ],
        1,
    ),
    (
        "a count that fell across two commits is a repair and is not judged",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "ccccccc", "created": "2026-08-27T13:05:58Z", "id": 1},
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 96, "sha": "ddddddd", "created": "2026-08-28T13:09:38Z", "id": 2},
        ],
        0,
    ),
    (
        "a category seen for the first time is not a category that lost anything",
        [
            {"category": "/language:actions", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 0, "sha": "eeeeeee", "created": "2026-08-29T10:15:40Z", "id": 1},
        ],
        0,
    ),
    (
        "a rerun at one commit reporting the same count is not a fall",
        [
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "fffffff", "created": "2026-08-27T13:05:58Z", "id": 1},
            {"category": "/language:csharp", "key": THIS_TREE + "scan-codeql.yaml:analyze", "results": 128, "sha": "fffffff", "created": "2026-08-27T14:00:00Z", "id": 2},
        ],
        0,
    ),
    (
        "an analysis carrying no key at all names no configuration and is refused",
        [
            {"category": "/language:csharp", "key": "", "results": 128, "sha": "ggggggg", "created": "2026-08-27T13:05:58Z", "id": 1},
        ],
        1,
    ),
)


def self_test():
    failed = 0
    for name, analyses, expected in FIXTURES:
        refused, _ = judge(analyses)
        if len(refused) == expected:
            print("ok    {0}".format(name))
        else:
            failed += 1
            print("FAIL  {0}".format(name))
            print("      expected {0} refusal(s), got {1}".format(expected, len(refused)))
            for line in refused:
                print("      {0}".format(line))
    print("{0} fixture(s), {1} failed.".format(len(FIXTURES), failed))
    return 1 if failed else 0


def main():
    if "--self-test" in sys.argv[1:]:
        return self_test()

    analyses = read_json_lines(ANALYSES)
    if not analyses:
        # An empty answer is not a clean branch. A repository whose analyses
        # cannot be read is exactly the state this exists to notice, and
        # passing it would be the failure it is about, one level up.
        print("::error::No analysis was read, so nothing here says the branch is clean.")
        return 1

    branch = os.environ.get("PROVENANCE_BRANCH", "the default branch")
    print("{0} analysis upload(s) read on {1}.".format(len(analyses), branch))

    refused, newest = judge(analyses)
    print("What each category stands on:")
    for line in describe(newest):
        print(line)

    if not refused:
        print("Every category stands on an analysis this tree produced.")
        return 0

    for line in refused:
        print("::error::{0}".format(line))
    print("{0} refusal(s).".format(len(refused)))
    return 1


if __name__ == "__main__":
    sys.exit(main())
