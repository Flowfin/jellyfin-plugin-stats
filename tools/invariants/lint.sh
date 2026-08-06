#!/usr/bin/env bash
#
# Reads tools/invariants/rules and greps the tree for each rule. A match fails
# the run. No language parser, no network, nothing but grep, so a rule costs a
# block in a text file rather than a change to this script.
#
#   bash tools/invariants/lint.sh              scan the tree
#   bash tools/invariants/lint.sh --self-test  prove every rule fires on its own
#                                              near miss and on no other rule's
#
# The self test is the half that keeps the scan honest. A rule whose pattern
# stopped matching anything still passes the scan, silently, forever.

set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
rules_file="$here/rules"
near_miss_root="$here/near-miss"

# The directory holding the rules is never scanned: it contains, by
# construction, every string the rules refuse.
exclusions=(--exclude-dir=.git --exclude-dir=bin --exclude-dir=obj --exclude-dir=invariants)

ids=() globs=() patterns=() whys=()
id="" glob="" pattern="" why=""

flush_rule() {
    if [ -n "$id" ]; then
        if [ -z "$pattern" ] || [ -z "$glob" ]; then
            echo "rules: '$id' is missing a paths or pattern field" >&2
            exit 2
        fi
        ids+=("$id"); globs+=("$glob"); patterns+=("$pattern"); whys+=("$why")
    fi
    id=""; glob=""; pattern=""; why=""
}

while IFS= read -r line || [ -n "$line" ]; do
    # A clone on Windows checks this file out with carriage returns, which turn
    # every blank separator line into a line the parser cannot place and every
    # pattern into one with a stray byte on the end. Measured on such a clone,
    # where the run stopped at the first blank line.
    line=${line%$'\r'}
    case "$line" in
        '#'*)      continue ;;
        '')        flush_rule ;;
        id:*)      id=${line#id:};           id=${id# } ;;
        paths:*)   glob=${line#paths:};      glob=${glob# } ;;
        pattern:*) pattern=${line#pattern:}; pattern=${pattern# } ;;
        why:*)     why=${line#why:};         why=${why# } ;;
        *)         echo "rules: cannot read line: $line" >&2; exit 2 ;;
    esac
done < "$rules_file"
flush_rule

if [ ${#ids[@]} -eq 0 ]; then
    echo "rules: no rule was read from $rules_file" >&2
    exit 2
fi

# grep exits 1 on no match, which is the passing case here, so every call is
# guarded rather than left to set -e.
scan() {
    local pattern=$1 glob=$2 where=$3
    grep -rnE --binary-files=without-match --include="$glob" "${exclusions[@]}" -- "$pattern" "$where" || true
}

if [ "${1:-}" = "--self-test" ]; then
    failed=0
    for i in "${!ids[@]}"; do
        dir="$near_miss_root/${ids[$i]}"
        if [ ! -d "$dir" ]; then
            echo "FAIL  ${ids[$i]}: no near miss recorded at ${dir#"$root"/}"
            failed=1
            continue
        fi
        own=$(scan "${patterns[$i]}" "${globs[$i]}" "$dir")
        if [ -z "$own" ]; then
            echo "FAIL  ${ids[$i]}: its near miss does not fire it"
            failed=1
            continue
        fi
        others=""
        for j in "${!ids[@]}"; do
            [ "$j" = "$i" ] && continue
            hit=$(scan "${patterns[$j]}" "${globs[$j]}" "$dir")
            [ -n "$hit" ] && others="$others ${ids[$j]}"
        done
        if [ -n "$others" ]; then
            echo "FAIL  ${ids[$i]}: its near miss also fires:$others"
            failed=1
            continue
        fi
        echo "ok    ${ids[$i]} fires on its near miss and on no other rule"
    done
    if [ "$failed" -ne 0 ]; then
        echo "The lint cannot prove that every rule bites." >&2
        exit 1
    fi
    echo "${#ids[@]} rule(s), each proved by a near miss of its own."
    exit 0
fi

cd "$root"
failed=0
for i in "${!ids[@]}"; do
    hits=$(scan "${patterns[$i]}" "${globs[$i]}" .)
    if [ -n "$hits" ]; then
        echo "FAIL  ${ids[$i]}"
        echo "      ${whys[$i]}"
        echo "$hits" | while IFS= read -r hit; do echo "      $hit"; done
        failed=1
    else
        echo "ok    ${ids[$i]}"
    fi
done

if [ "$failed" -ne 0 ]; then
    echo "An invariant is violated. Fix the code, or argue the rule out of tools/invariants/rules." >&2
    exit 1
fi
echo "${#ids[@]} rule(s) checked over the tree, no match."
