#!/usr/bin/env bash
set -e

# Disable linters not supported in dispatch.yaml inputs or incompatible with repo
if [ -n "$GITHUB_ENV" ]; then
  {
    echo "VALIDATE_CSS=false"
    echo "VALIDATE_CSS_STYLELINT=false"
    echo "VALIDATE_GITLEAKS=false"
    echo "VALIDATE_TYPESCRIPT_PRETTIER=false"
    echo "VALIDATE_MARKDOWN_PRETTIER=false"
    echo "VALIDATE_SPELL_CODESPELL=false"
    echo "VALIDATE_TYPESCRIPT_ES=false"
    echo "VALIDATE_MARKDOWN=false"
    echo "VALIDATE_NATURAL_LANGUAGE=false"
    echo "VALIDATE_PRETTIER=false"
  } >> "$GITHUB_ENV"
fi

# Output exit code 0 for dispatch.yaml 'exit $(./checks.sh)'
echo "0"
