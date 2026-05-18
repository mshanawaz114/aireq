#!/usr/bin/env bash
#
# scripts/push-pr.sh — push the current branch and open a PR in one command.
#
# Usage:
#   ./scripts/push-pr.sh                       # title auto-filled from commit
#   ./scripts/push-pr.sh "AIR0001 short desc"  # explicit title
#
# Requirements:
#   - gh (GitHub CLI) installed and authenticated (`gh auth login`)
#   - On a non-main branch with at least one commit ahead of origin
#
# Behavior:
#   1. Refuses to run from main / master / release branches.
#   2. Verifies branch name matches one of the project conventions
#      (AIR####-*, AIRMVP{N}-*, AIRGA1-*, AIRSCALE-*, hotfix/*, release/*).
#   3. Pushes the branch upstream (sets upstream on first push).
#   4. If no PR exists yet for this branch, creates one against main
#      using --fill (commit message → PR body) and the project PR template.
#   5. Opens the PR in your browser for final review.
#
# Exit codes:
#   0   success
#   1   ran from a protected branch
#   2   branch name does not match convention
#   3   git push failed
#   4   gh pr create failed
#
set -euo pipefail

BRANCH="$(git rev-parse --abbrev-ref HEAD)"

# 1. Refuse protected branches
case "$BRANCH" in
  main|master)
    echo "refuse: cannot push-pr from '$BRANCH'." >&2
    exit 1
    ;;
esac

# 2. Validate branch convention
if ! [[ "$BRANCH" =~ ^(AIR[0-9]{4}|AIRMVP[0-9]+|AIRGA[0-9]+|AIRSCALE)-.+ ]] \
   && ! [[ "$BRANCH" =~ ^hotfix/.+ ]] \
   && ! [[ "$BRANCH" =~ ^release/v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  cat >&2 <<EOF
refuse: branch '$BRANCH' does not match a project convention.

Expected one of:
  AIR####-<slug>                e.g. AIR0001-initial-workflow
  AIRMVP{N}-<story-id>-<slug>   e.g. AIRMVP1-103-tenant-middleware
  AIRGA{N}-<story-id>-<slug>    e.g. AIRGA1-140-axe-cert
  AIRSCALE-<story-id>-<slug>    e.g. AIRSCALE-201-browser-extension
  hotfix/<short>                e.g. hotfix/jwt-clock-skew
  release/v<X.Y.Z>              e.g. release/v0.1.0

Rename your branch:
  git branch -m <new-name>
EOF
  exit 2
fi

# 3. Push (sets upstream on first push)
echo ">> pushing $BRANCH to origin..."
if ! git push -u origin "$BRANCH"; then
  echo "error: git push failed." >&2
  exit 3
fi

# 4. Check for an existing PR
EXISTING_PR_URL="$(gh pr list --head "$BRANCH" --json url --jq '.[0].url' 2>/dev/null || true)"

if [[ -n "${EXISTING_PR_URL:-}" ]]; then
  echo ">> PR already exists: $EXISTING_PR_URL"
  gh pr view --web
  exit 0
fi

# 5. Create the PR
TITLE="${1:-}"
echo ">> creating PR against main..."
if [[ -n "$TITLE" ]]; then
  if ! gh pr create --base main --fill --title "$TITLE" --web; then
    echo "error: gh pr create failed." >&2
    exit 4
  fi
else
  if ! gh pr create --base main --fill --web; then
    echo "error: gh pr create failed." >&2
    exit 4
  fi
fi

echo ">> done."
