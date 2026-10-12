## Communication (mandatory)

- Do **not** use Slack MCP or any external messaging (Slack, Gmail send, Telegram, etc.).
- Report completion **only** to the swarm coordinator via `swarm` / `DONE #<issue>` (internal report_back).
- This repo's `.jcode/` config disables Slack MCP; `pre_tool` hooks block slack tool calls.

## Git / delivery (mandatory)

- **Never open, update, or comment on GitHub pull requests.** Do not call `github_create_pr`, `gh pr create`, or any PR MCP tool.
- **Never push a remote feature branch.** Do not run `git push -u origin issue-*` or `git push origin issue-*`.
- Use your assigned worktree on a **local-only** branch `issue-<id>` for isolation if needed. Do not create that branch on the remote.
- Before finishing: `git fetch origin main`, **rebase onto `origin/main`**, run targeted tests.
- Land on `main` by pushing **only** `main`: either merge/rebase your commits onto local `main` and `git push origin main`, or after a clean rebase onto `origin/main` use `git push origin HEAD:main` (never push `issue-<id>`).
- If push is rejected, pull/rebase `origin/main` again, resolve conflicts, retest, push `main` again.
- Report `DONE #<id>` with the `origin/main` commit SHA. Close the GitHub issue when verified on `main`.
