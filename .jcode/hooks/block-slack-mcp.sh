#!/usr/bin/env bash
# Leecharr orchestrator workers: block Slack MCP (no DMs/posts).
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
    ;;
  mcp__slack__*|cc_mcp__slack__*)
    echo "blocked: Slack MCP is disabled for Leecharr swarm workers. Report completion only via swarm (DONE #issue)." >&2
    exit 2
    ;;
esac

exit 0
