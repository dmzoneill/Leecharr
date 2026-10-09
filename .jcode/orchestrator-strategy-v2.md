# Leecharr issue orchestrator v2

## Why v1 failed

Workers were prompted to track progress in `.jcode/orchestrator-state.json`. Five agents wrote same file in main repo. Coordinator also wrote it. Git merge fights, stale slots, plan drift.

## Principles

1. **Single writer** — Only coordinator session (`sauropod`) may edit `.jcode/orchestrator-state.json`.
2. **Workers report only** — Workers use `swarm report` (and GitHub issue comments). Never open `.jcode/*` unless coordinator asks.
3. **Isolation** — All code changes in `../leecharr-wt-{issue}/` only. No edits under main repo `src/` while orchestrator runs.
4. **No `run_plan` while manual spawn** — Pick one: light plan with fixed nodes, or coordinator `spawn` + `assign_task`. Mixing caused double-assign and stale Active nodes.
5. **Truth from GitHub** — Coordinator reconciles issue `state=open|closed` and last commit on `main` each poll. Local JSON is cache, not authority.
6. **Push policy** — Parallel push to `main` races. v2 default: one worker pushes at a time (coordinator grants `push_lock` in state), or workers push only after coordinator DM "push now".

## Coordinator loop (every 30–60s)

1. `swarm list` — running / ready / completed / failed.
2. GitHub: verify open/closed for `in_progress` issue numbers.
3. Update `orchestrator-state.json` (coordinator only).
4. If worker `completed` or `failed` >2m idle: cleanup session or DM new issue; spawn replacement to keep 5.
5. Assign next issue by **area** to reduce file overlap.

## Worker prompt template (mandatory lines)

```
REPO: dmzoneill/Leecharr
ISSUE: #{n}
WORKTREE: /home/daoneill/src/usr/leecharr-wt-{n}
DO NOT edit /home/daoneill/src/usr/leecharr/.jcode/orchestrator-state.json
DO NOT edit files outside your worktree
Validate issue + comments; close with reason if invalid
No local test/build; lint/format OK
Push to main only when coordinator says push, OR after fix ready if solo mode
On done: GitHub comment + close issue; swarm report DONE #{n}
```

## Resume checklist

1. Confirm `swarm list` shows no stray workers (`swarm cleanup`).
2. Set `orchestrator-state.json` → `status: running`, fill `in_progress` from GitHub truth.
3. Spawn 5 workers with template above; **do not** `run_plan` unless plan cleared.
4. Re-enable schedule poll on coordinator only.

## Halted

2026-10-09: all workers stopped; v1 plan items completed; awaiting user OK to resume v2.
