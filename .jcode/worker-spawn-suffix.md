## Communication (mandatory)

- Do **not** use Slack MCP or any external messaging (Slack, Gmail send, Telegram, etc.).
- Report completion **only** to the swarm coordinator via `swarm` / `DONE #<issue>` (internal report_back).
- This repo's `.jcode/` config disables Slack MCP; `pre_tool` hooks block slack tool calls.

## Git / delivery (mandatory)

- **Never open, update, or comment on GitHub pull requests.** Do not call `github_create_pr`, `gh pr create`, or any PR MCP tool.
- Work on branch `issue-<id>` (or the assigned feature branch) in your worktree only.
- Integrate via **rebase onto `origin/main`**, run tests locally, then **push the branch** for orchestrator merge if a legacy PR exists, or follow coordinator **READY PUSH** / main-only policy when instructed.
- Close the GitHub issue with a comment when the fix is on `main` and verified.
