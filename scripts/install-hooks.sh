#!/usr/bin/env bash
#
# scripts/install-hooks.sh — install the project's pre-commit hooks.
#
# Idempotent: safe to run multiple times. Requires Python 3.10+ (for the
# `pre-commit` package) and that `pip` is on PATH.
#
# What it installs:
#   - Hooks declared in .pre-commit-config.yaml
#   - A commit-msg hook that enforces Conventional Commits
#
# Refs: AIR0003

set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

# 1. Ensure `pre-commit` is available. Don't pollute the global Python env
#    if the user has pipx — prefer it. Fall back to pip --user.
if ! command -v pre-commit >/dev/null 2>&1; then
    echo ">> pre-commit not on PATH; installing..."
    if command -v pipx >/dev/null 2>&1; then
        pipx install pre-commit
    elif command -v pip >/dev/null 2>&1; then
        pip install --user pre-commit
        # Make sure ~/.local/bin is on PATH for this shell.
        export PATH="$HOME/.local/bin:$PATH"
    else
        echo "error: neither pipx nor pip is available. Install one and retry." >&2
        exit 1
    fi
fi

# 2. Install the git hooks. `--install-hooks` warms the pre-commit cache so
#    the first real commit is fast.
pre-commit install --install-hooks --hook-type pre-commit --hook-type commit-msg

# 3. Smoke test: run against ALL files once. Any leftover lint debt surfaces
#    here rather than blocking someone's next commit.
echo ">> running all hooks once against the full tree (smoke test)..."
pre-commit run --all-files || {
    echo ""
    echo ">> some hooks reported issues. They may have auto-fixed files —"
    echo "   review with 'git status' and re-stage as needed. Hooks are installed regardless."
    exit 0
}

echo ">> pre-commit hooks installed and clean."
