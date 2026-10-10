#!/usr/bin/env bash
# Point this clone (and all linked worktrees) at repo .githooks via absolute path.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
hooks_dir="${repo_root}/.githooks"

if [[ ! -d "$hooks_dir" ]]; then
  echo "Missing ${hooks_dir}" >&2
  exit 1
fi

chmod +x "${hooks_dir}"/commit-msg "${hooks_dir}"/prepare-commit-msg 2>/dev/null || true

git -C "$repo_root" config core.hooksPath "$hooks_dir"

echo "core.hooksPath=${hooks_dir}"
echo "Hooks active for main repo and all worktrees sharing this .git config."
