# Leecharr swarm workers (v2)

## Git push (mandatory)

- **Never** run `git push origin issue-*` or push any remote branch besides `main`.
- Keep commits on local branch `issue-{n}` in your worktree until coordinator grants push.
- On **push now** from coordinator (`sauropod`):
  1. `git fetch origin`
  2. Rebase your branch onto `origin/main`
  3. `git push origin main` only (fast-forward or rebase flow your worktree uses)
  4. Comment on GitHub issue + close
  5. `swarm report` DONE #{n}
  6. If `origin/issue-{n}` exists from older runs: `git push origin :issue-{n}`
- **NO PR.** Coordinator owns `.jcode/orchestrator-state.json`; workers do not edit it.

## Isolation

- Code changes only under `/home/daoneill/src/usr/leecharr-wt-{issue}/`.
