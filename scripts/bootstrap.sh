#!/usr/bin/env bash
#
# scripts/bootstrap.sh — one-shot dev environment setup.
# Idempotent: safe to run repeatedly.
#
# What it does:
#   1. Verifies required tools (.NET 10 SDK, Node 22+, pnpm 9+, gh).
#   2. Copies .env.example → .env.local if missing.
#   3. Installs .NET tools (dotnet-ef).
#   4. Installs Next.js dependencies.
#   5. Tells you what's left for you to do (cloud accounts).
#

set -euo pipefail

# --- Colors ----------------------------------------------------------------
if [[ -t 1 ]]; then
  BOLD=$(printf '\033[1m'); RESET=$(printf '\033[0m')
  RED=$(printf '\033[31m'); GREEN=$(printf '\033[32m'); YELLOW=$(printf '\033[33m'); BLUE=$(printf '\033[34m')
else
  BOLD=""; RESET=""; RED=""; GREEN=""; YELLOW=""; BLUE=""
fi

header() { printf "\n${BOLD}${BLUE}==> %s${RESET}\n" "$1"; }
ok()     { printf "  ${GREEN}✓${RESET} %s\n" "$1"; }
warn()   { printf "  ${YELLOW}!${RESET} %s\n" "$1"; }
fail()   { printf "  ${RED}✗${RESET} %s\n" "$1"; exit 1; }

cd "$(dirname "$0")/.."

# --- Step 1: required tools ------------------------------------------------
header "Checking required tools"

command -v dotnet >/dev/null 2>&1 || fail ".NET SDK missing. Install from https://dotnet.microsoft.com/download/dotnet/10.0"
DOTNET_VER=$(dotnet --version)
case "$DOTNET_VER" in
  10.*)  ok ".NET SDK $DOTNET_VER" ;;
  *)     fail ".NET 10 SDK required (found $DOTNET_VER). Install from https://dotnet.microsoft.com/download/dotnet/10.0" ;;
esac

command -v node >/dev/null 2>&1 || fail "Node.js missing. Install Node 22 LTS from https://nodejs.org/"
NODE_VER=$(node -v)
case "$NODE_VER" in
  v22.*|v23.*|v24.*) ok "Node $NODE_VER" ;;
  *) fail "Node 22+ required (found $NODE_VER). Install from https://nodejs.org/" ;;
esac

if ! command -v pnpm >/dev/null 2>&1; then
  warn "pnpm not found — installing via corepack"
  corepack enable
  corepack prepare pnpm@9 --activate
fi
ok "pnpm $(pnpm -v)"

if ! command -v gh >/dev/null 2>&1; then
  warn "GitHub CLI (gh) not found — needed for scripts/push-pr.sh."
  warn "  Install: brew install gh    (or)    https://cli.github.com/"
else
  ok "gh $(gh --version | head -1 | awk '{print $3}')"
fi

# --- Step 2: .env.local ----------------------------------------------------
header "Local config"
if [[ ! -f .env.local ]]; then
  cp .env.example .env.local
  warn ".env.local created from .env.example — open it and fill in real values."
else
  ok ".env.local exists"
fi

# --- Step 3: .NET tools ----------------------------------------------------
header "Installing .NET tools"
if ! dotnet tool list -g | grep -q dotnet-ef; then
  dotnet tool install -g dotnet-ef --version 10.0.0
  ok "dotnet-ef installed"
else
  ok "dotnet-ef already installed"
fi

# --- Step 4: NuGet restore -------------------------------------------------
header "Restoring .NET packages"
dotnet restore Aireq.sln --nologo
ok "NuGet restore complete"

# --- Step 5: pnpm install --------------------------------------------------
header "Installing web dependencies"
( cd apps/web && pnpm install --frozen-lockfile 2>/dev/null || pnpm install )
ok "pnpm install complete"

# --- Step 6: Final guidance ------------------------------------------------
header "Almost done"
cat <<'EOF'

What's left for you (manual steps that need a human at a portal):

  1. Open SETUP_CLOUD.md and follow the four sections:
       - Neon Postgres project + dev branch
       - Anthropic Claude API key (with a $30 cap)
       - Resend domain verification (DKIM)
       - Azure resource-group sanity check

  2. Paste the connection string and keys into .env.local.

  3. Boot the stack:
       make dev

  4. Open http://localhost:3000 — you should see the dashboard, with a
     green "API ok" badge in the top-right (meaning the API can reach Neon).

When the badge is green, AIRMVP1-101 is done. Cut AIRMVP1-102 next.

EOF
