#!/usr/bin/env bash
# For each open issue labeled in_progress, detect fixes already on origin/main.
set -euo pipefail
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"
git fetch origin main -q 2>/dev/null || true
MAIN=origin/main
issues=$(gh issue list --label in_progress --state open --json number -q '.[].number' 2>/dev/null || true)
for n in $issues; do
  closed=$(gh issue view "$n" --json state -q .state 2>/dev/null || echo OPEN)
  if [[ "$closed" == "CLOSED" ]]; then
    echo "CLOSED_ON_GH $n"
    continue
  fi
  # Non-chore fix commits on main mentioning the issue
  if git log "$MAIN" --oneline --grep="#${n}" --grep="fix" --all-match -1 2>/dev/null | grep -q .; then
    sha=$(git log "$MAIN" --oneline --grep="#${n}" --grep="fix" --all-match -1 --format=%H)
    echo "FIX_ON_MAIN $n $sha"
    continue
  fi
  if git log "$MAIN" --oneline --grep="(#${n})" -1 2>/dev/null | grep -q .; then
    sha=$(git log "$MAIN" --oneline --grep="(#${n})" -1 --format=%H)
    echo "FIX_ON_MAIN $n $sha"
    continue
  fi
  # fix(N): style without hash
  if git log "$MAIN" --oneline -50 | grep -qiE "fix.*\b${n}\b|#${n}\b"; then
    line=$(git log "$MAIN" --oneline -50 | grep -iE "fix.*\b${n}\b|#${n}\b" | head -1)
    echo "LIKELY_ON_MAIN $n $line"
    continue
  fi
  echo "PENDING $n"
done
