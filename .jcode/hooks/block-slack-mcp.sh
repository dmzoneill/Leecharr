#!/usr/bin/env bash
# Leecharr orchestrator workers: block Slack MCP and GitHub PR creation.
set -euo pipefail

tool="${JCODE_HOOK_TOOL_NAME:-}"
input="${JCODE_HOOK_TOOL_INPUT:-}"
if [[ -z "$input" ]]; then
  input="$(cat)"
fi

case "$tool" in
  mcp_call|mcp)
    if grep -qE '"server"\s*:\s*"slack"' <<<"$input"; then
      echo "blocked: Slack MCP is disabled for Leecharr swarm workers. Report completion only via swarm (DONE #issue)." >&2
      exit 2
    fi
    if grep -qE 'github_create_pr|create_pull_request|pull_request.*create' <<<"$input"; then
      echo "blocked: GitHub PR creation is forbidden for Leecharr workers. Push your branch; orchestrator merges. Never open PRs." >&2
      exit 2
    fi
    ;;
  mcp__slack__*|cc_mcp__slack__*)
    echo "blocked: Slack MCP is disabled for Leecharr swarm workers. Report completion only via swarm (DONE #issue)." >&2
    exit 2
    ;;
  github_create_pr|cc_github_create_pr|mcp__github__*create*pr*)
    echo "blocked: GitHub PR creation is forbidden for Leecharr workers. Push your branch only." >&2
    exit 2
    ;;
esac

if grep -qE '\bgh pr create\b' <<<"$input"; then
  echo "blocked: gh pr create is forbidden for Leecharr workers." >&2
  exit 2
fi

exit 0
