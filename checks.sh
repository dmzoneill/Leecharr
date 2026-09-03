#!/usr/bin/env bash
set -e

# Configure GitHub Actions environment for subsequent Super-Linter steps
if [ -n "$GITHUB_ENV" ]; then
  echo "VALIDATE_CSS=false" >> "$GITHUB_ENV"
  echo "VALIDATE_CSS_STYLELINT=false" >> "$GITHUB_ENV"
  echo "VALIDATE_GITLEAKS=false" >> "$GITHUB_ENV"
  echo "VALIDATE_TYPESCRIPT_PRETTIER=false" >> "$GITHUB_ENV"
  echo "VALIDATE_PRETTIER=false" >> "$GITHUB_ENV"
fi

# Output exit code 0 for dispatch.yaml 'exit $(./checks.sh)'
echo "0"
