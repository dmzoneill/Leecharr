#!/usr/bin/env bash
# Use system-wide git hooks (Co-authored-by blocked on every repo).
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
global_hooks="${HOME}/.git-hooks"

git -C "$repo_root" config --unset core.hooksPath 2>/dev/null || true

git config --global core.hooksPath "$global_hooks"
chmod +x "${global_hooks}"/commit-msg "${global_hooks}"/prepare-commit-msg 2>/dev/null || true
chmod +x "${HOME}/.local/bin/git-strip-all-coauthored.sh" \
  "${HOME}/.local/bin/git-reject-coauthored-and-bots.sh" 2>/dev/null || true

echo "global core.hooksPath=${global_hooks}"
echo "This repo uses global hooks (no per-repo core.hooksPath override)."
