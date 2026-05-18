# REPO_INIT.md — Day 0 setup for Aireq

> Copy-paste this top-to-bottom. ~15 minutes total. After this you'll have:
> - GitHub repo `aireq` initialized.
> - .NET 10 solution with `api` + `worker` projects.
> - Next.js 15 web app.
> - `memory.md`, `PLAN.md`, README, .gitignore, GitHub Actions CI stub.
> - First commit pushed.
>
> **Pre-requisites already installed:** .NET 10 SDK, Node 22 LTS, Git, GitHub CLI (`gh`), VS Code or Cursor.

---

## 0. Confirm versions

```bash
dotnet --version    # expect 10.x
node -v             # expect v22.x
git --version
gh --version
```

If any are missing:
- .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
- Node 22 LTS: https://nodejs.org/
- GitHub CLI: `brew install gh` (macOS) or `winget install GitHub.cli` (Windows)

---

## 1. Create the GitHub repo

```bash
gh auth login                                    # if not already
mkdir aireq && cd aireq
git init -b main
gh repo create aireq --private --source=. --remote=origin
```

---

## 2. Scaffold the folder layout

```bash
mkdir -p apps/api apps/worker apps/web packages/shared infra/bicep infra/github docs
```

---

## 3. Create the .NET 10 solution + projects

```bash
dotnet new sln -n Aireq
cd apps/api && dotnet new webapi --use-minimal-apis -n Aireq.Api && cd ../..
cd apps/worker && dotnet new worker -n Aireq.Worker && cd ../..
cd packages/shared && dotnet new classlib -n Aireq.Shared && cd ../..

dotnet sln Aireq.sln add apps/api/Aireq.Api/Aireq.Api.csproj
dotnet sln Aireq.sln add apps/worker/Aireq.Worker/Aireq.Worker.csproj
dotnet sln Aireq.sln add packages/shared/Aireq.Shared/Aireq.Shared.csproj

# Wire dependencies
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj reference packages/shared/Aireq.Shared/Aireq.Shared.csproj
dotnet add apps/worker/Aireq.Worker/Aireq.Worker.csproj reference packages/shared/Aireq.Shared/Aireq.Shared.csproj

# Core NuGet packages
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Microsoft.EntityFrameworkCore --prerelease
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Npgsql.EntityFrameworkCore.PostgreSQL --prerelease
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Pgvector.EntityFrameworkCore --prerelease
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Microsoft.AspNetCore.Authentication.JwtBearer --prerelease
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Microsoft.AspNetCore.Identity.EntityFrameworkCore --prerelease
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Hangfire.AspNetCore
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Hangfire.PostgreSql
dotnet add apps/api/Aireq.Api/Aireq.Api.csproj package Serilog.AspNetCore

dotnet add apps/worker/Aireq.Worker/Aireq.Worker.csproj package Microsoft.Playwright
dotnet add apps/worker/Aireq.Worker/Aireq.Worker.csproj package Hangfire.PostgreSql
```

---

## 4. Scaffold the Next.js 15 web app

```bash
cd apps
pnpm create next-app@latest web --typescript --tailwind --eslint --app --src-dir --no-import-alias --turbopack
cd web
pnpm add @tanstack/react-query zod react-hook-form lucide-react clsx tailwind-merge
pnpm add -D @types/node
cd ../..
```

(If you prefer npm/yarn, swap `pnpm` accordingly.)

---

## 5. Drop in the project files

Copy these three files from this outputs folder into the repo root:

```bash
# from your laptop (adjust the path to wherever Cowork wrote them):
cp /path/to/cowork/outputs/memory.md       ./memory.md
cp /path/to/cowork/outputs/PLAN.md         ./docs/PLAN.md
cp /path/to/cowork/outputs/prototype.html  ./docs/prototype.html
```

---

## 6. Create `.gitignore`

```bash
cat > .gitignore <<'EOF'
# .NET
bin/
obj/
*.user
*.suo
.vs/

# Node
node_modules/
.next/
out/
dist/
.turbo/
.pnpm-store/

# IDE
.idea/
.vscode/launch.json
.vscode/tasks.json

# Env
.env
.env.local
.env.*.local
*.local

# OS
.DS_Store
Thumbs.db

# Logs
*.log
EOF
```

---

## 7. Create `.env.local.example`

```bash
cat > .env.local.example <<'EOF'
# Postgres (Neon)
DATABASE_URL_DEV=postgresql://USER:PASS@HOST/aireq_dev?sslmode=require
DATABASE_URL_PROD=postgresql://USER:PASS@HOST/aireq_prod?sslmode=require

# Auth
JWT_SIGNING_KEY=replace-me-with-32-bytes-of-random
JWT_ISSUER=aireq
JWT_AUDIENCE=aireq-web

# LLM
ANTHROPIC_API_KEY=sk-ant-...
ANTHROPIC_MAX_MONTHLY_USD=20

# Email
RESEND_API_KEY=re_...

# Storage
AZURE_BLOB_CONNECTION_STRING=DefaultEndpointsProtocol=https;...

# Job source APIs
ADZUNA_APP_ID=...
ADZUNA_APP_KEY=...
USAJOBS_USER_AGENT=your.email@example.com
USAJOBS_AUTH_KEY=...

# Gmail OAuth (per-tenant; this is the dev app credential)
GOOGLE_CLIENT_ID=...
GOOGLE_CLIENT_SECRET=...
EOF
```

---

## 8. Create the README

```bash
cat > README.md <<'EOF'
# Aireq

> **AI Operations Copilot for staffing agencies and consultants.**
> Pronunciation: *AI · req* ("ay-eye-rek").

Aireq markets consultants automatically — discovers real openings, tailors the resume per role to beat ATS, auto-submits via Playwright + APIs, sends follow-ups, and only escalates when a human is needed.

## Quickstart (dev)

```bash
# 1. Restore + build
dotnet restore
dotnet build

# 2. Frontend
cd apps/web && pnpm install && pnpm dev

# 3. Backend (separate terminal)
cd apps/api/Aireq.Api && dotnet watch run

# 4. Worker (separate terminal)
cd apps/worker/Aireq.Worker && dotnet watch run
```

## Repo layout

```
apps/
  api/          ASP.NET Core 10 minimal API (auth, REST, SignalR)
  worker/       ASP.NET Core 10 worker (Hangfire + Playwright)
  web/          Next.js 15 (App Router, Tailwind, shadcn/ui)
packages/
  shared/       shared domain types
infra/          bicep + GH Actions
docs/           plan, architecture, prototype
memory.md       persistent project context — READ FIRST
```

## Source of truth

- **`memory.md`** — every project decision lives here. Read this before any work.
- **`docs/PLAN.md`** — 4-week build plan, day-by-day.
- **`docs/prototype.html`** — clickable UI prototype (open in browser).

## Stack

.NET 10 LTS · Next.js 15 · Neon Postgres + pgvector · Hangfire · Playwright · Claude (Haiku + Sonnet) · Azure (App Service / Container Apps free tier) · Vercel · Resend · GitHub Actions · Jira.

## License

Proprietary. © 2026 shahnawaz. All rights reserved.
EOF
```

---

## 9. GitHub Actions CI stub

```bash
mkdir -p .github/workflows
cat > .github/workflows/ci.yml <<'EOF'
name: ci

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
          dotnet-quality: preview
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release --verbosity normal

  web:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: apps/web
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
        with: { version: 9 }
      - uses: actions/setup-node@v4
        with: { node-version: 22, cache: pnpm, cache-dependency-path: apps/web/pnpm-lock.yaml }
      - run: pnpm install --frozen-lockfile
      - run: pnpm lint
      - run: pnpm build
EOF
```

---

## 10. Jira project quickstart

In Jira (free plan):

1. Create site `aireq.atlassian.net`.
2. Create project → **Scrum** template → name **Aireq** → key **`AIR`**.
3. Create 7 epics (paste in one go via CSV import or click-create):

   - `AIR-E1` Week 1 — Foundations
   - `AIR-E2` Week 2 — Job Discovery
   - `AIR-E3` Week 3 — Tailor & Apply
   - `AIR-E4` Week 4 — CRM & Polish
   - `AIR-E10` X — Auth & Multi-tenancy
   - `AIR-E11` X — AI Gateway & Cost Caps
   - `AIR-E12` X — Observability & Logging

4. Each "Day N" in `docs/PLAN.md` becomes one Story under the matching week epic.
5. Sprint length = 1 week. Sprint 1 starts the day this script finishes.

---

## 11. Domains & email

Buy domains (verify availability at registrar first):

- `aireq.com` (primary — web + app + email)
- `aireq.ai` (alternate web)
- `aireq.io` (defensive)

Recommended registrar: **Cloudflare Registrar** (at-cost pricing, free WHOIS privacy, free DNS) or **Namecheap**.

DNS records to add immediately (Cloudflare DNS):

```
A     aireq.com           76.76.21.21              (Vercel — placeholder; replace after Vercel onboard)
CNAME app.aireq.com       cname.vercel-dns.com.
CNAME api.aireq.com       <azure-container-app-fqdn>
MX    aireq.com           feedback-smtp.us-east-1.amazonses.com (priority 10)   (for Resend)
TXT   aireq.com           "v=spf1 include:amazonses.com ~all"
TXT   _dmarc.aireq.com    "v=DMARC1; p=quarantine; rua=mailto:dmarc@aireq.com"
CNAME <resend-dkim-record>    <resend-dkim-target>                                   (Resend will give you this)
```

---

## 12. First commit

```bash
git add -A
git commit -m "chore: scaffold Aireq monorepo (.NET 10 + Next.js 15)"
git push -u origin main
```

You now have:
- Empty-but-buildable .NET 10 solution.
- Empty-but-buildable Next.js 15 app.
- CI passing on push.
- `memory.md` checked into the repo (any future AI/agent reads it first).
- Public roadmap in `docs/PLAN.md`.

**Next:** Open Day 1 in `docs/PLAN.md` and begin.

---

## Quick sanity-check matrix

| Item | Done? | How to verify |
|---|---|---|
| `.com` and `.ai` domains owned | ☐ | Registrar dashboard |
| GitHub repo exists | ☐ | `gh repo view aireq --web` |
| `dotnet build` succeeds | ☐ | exit code 0 |
| `pnpm build` in apps/web succeeds | ☐ | exit code 0 |
| First commit pushed | ☐ | `git log --oneline -5` |
| CI green on main | ☐ | GitHub Actions tab |
| `memory.md` in repo root | ☐ | `ls memory.md` |
| Neon project created | ☐ | console.neon.tech |
| Anthropic key created with $30 cap | ☐ | console.anthropic.com |
| Resend account + DKIM passing | ☐ | resend.com/domains |
| Jira project created with 7 epics | ☐ | `aireq.atlassian.net` |
