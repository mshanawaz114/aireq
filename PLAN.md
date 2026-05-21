# Aireq — Phase Plan, Epics & Stories

> Companion to `memory.md`. **`memory.md` is source of truth for decisions; this file is source of truth for execution.**
> Every Story has a stable ID. Every branch and commit references its Story ID.

**Brand:** Aireq · **Pronunciation:** *AI-rek* · **Repo:** `github.com/mshanawaz114/aireq`
**Stack:** .NET 10 LTS · Next.js 15 · Neon Postgres + pgvector · Playwright · Hangfire · Azure free tier · Claude API · Resend

---

## Table of contents

1. [Phase 0 — Foundations](#phase-0--foundations) (now)
2. [Phase 1 — MVP v1](#phase-1--mvp-v1) (Weeks 1-4)
3. [Phase 2 — Multi-user GA](#phase-2--multi-user-ga) (Weeks 5-8)
4. [Phase 3 — Scale & moat](#phase-3--scale--moat) (Months 3-6)
5. [Cross-cutting epics](#cross-cutting-epics)
6. [Tooling matrix](#tooling-matrix)
7. [Definition of Done (universal)](#definition-of-done-universal)

---

## Phase 0 — Foundations

**Goal:** Professional, accessible, secure, contributor-ready repo. Everything an AI agent or human needs to start Day 1 of MVP work, without re-asking.
**Branch prefix:** `AIR####-<slug>`
**Duration:** Day 0 (today).

### Epic AIR-E0 — Repo Foundations

| Story ID | Title | Branch | Status |
|---|---|---|---|
| `AIR0001` | Initial workflow — docs, governance, conventions | `AIR0001-initial-workflow` | **in progress (this branch)** |
| `AIR0002` | CI/CD pipelines (build, lint, test, a11y, security) | `AIR0002-ci-cd-pipelines` | pending |
| `AIR0003` | Secret management & pre-commit hooks (gitleaks) | `AIR0003-secret-management` | pending |
| `AIR0004` | Issue/PR templates, CODEOWNERS, Dependabot config | `AIR0004-github-governance` | pending |
| `AIR0005` | Accessibility tooling (axe-core, pa11y-ci) wired in CI | `AIR0005-a11y-tooling` | pending |
| `AIR0006` | Architecture Decision Records (ADRs) seeded | `AIR0006-seed-adrs` | pending |
| `AIR0007` | Skill & plugin scaffold for Cowork/Claude tools | `AIR0007-skills-plugins` | pending |

> **AIR0001** (this branch) folds AIR0002–AIR0007's *scaffolding* into a single foundational commit. Each subsequent AIR000X story can then iterate on its own branch as needed.

---

## Phase 1 — MVP v1

**Goal:** End-to-end: owner uploads a real resume → system discovers ≥50 real matches → tailors per JD → submits via API/Playwright → follow-up email → recruiter reply classified → human escalation only when needed.
**Branch prefix:** `AIRMVP1-<story-id>-<slug>`
**Duration:** Weeks 1-4.

### Epic AIRMVP1-W1 — Foundation & Resume Intelligence (Week 1)

| Story ID | Day | Title | Owner | Estimate |
|---|---|---|---|---|
| `AIRMVP1-101` | D1 | Repo skeleton — .NET 10 solution + Next.js 15 app + workspace tooling | dev | 4h |
| `AIRMVP1-102` | D2 | Domain model + EF Core migrations + pgvector enabled on Neon | dev | 5h |
| `AIRMVP1-103` | D3 | Auth + multi-tenant middleware + cross-tenant isolation test | dev | 6h |
| `AIRMVP1-104` | D4 | Resume upload + Azure Blob (Azurite local) + Hangfire enqueue | dev | 4h |
| `AIRMVP1-105` | D5 | Resume parsing via `LlmGateway` (Claude Haiku) | dev | 6h |
| `AIRMVP1-106` | D6 | Web UI shell — login, dashboard, upload, profile (a11y baseline) | dev | 5h |
| `AIRMVP1-107` | D7 | Deploy `api` + `worker` to Azure Container Apps, `web` to Vercel | dev | 4h |

**Epic gate:** owner can sign up at the public URL, upload a real resume, see parsed profile rendered. Bill ≤ $1.

### Epic AIRMVP1-W2 — Job Discovery & Matching (Week 2)

| Story ID | Day | Title | Estimate |
|---|---|---|---|
| `AIRMVP1-201` | D8  | Adzuna + USAJobs ingestion jobs (Hangfire recurring) | 5h |
| `AIRMVP1-202` | D9  | Greenhouse / Lever / Ashby ingestion (50 seeded companies) | 6h |
| `AIRMVP1-203` | D10 | Dedupe + freshness pipeline | 4h |
| `AIRMVP1-204` | D11 | Embeddings + pgvector cosine matching + rule filters | 5h |
| `AIRMVP1-205` | D12 | Match scoring + reasoning (Haiku) | 4h |
| `AIRMVP1-206` | D13 | Matches UI with filters and "Tailor & apply" CTA | 5h |
| `AIRMVP1-207` | D14 | Events analytics + admin metrics dashboard | 3h |

**Epic gate:** owner sees ≥ 50 real matches with scores + explanations. Bill ≤ $5.

### Epic AIRMVP1-W3 — Tailor, ATS & Auto-Apply (Week 3)

| Story ID | Day | Title | Estimate |
|---|---|---|---|
| `AIRMVP1-301` | D15 | ATS keyword extractor + missing-keywords UI panel | 4h |
| `AIRMVP1-302` | D16 | Resume rewriter (Sonnet) + tailored PDF render | 6h |
| `AIRMVP1-303` | D17 | Tier A submission — Greenhouse + Lever submit APIs | 5h |
| `AIRMVP1-304` | D18 | Tier B submission — Playwright per-ATS templates (dry-run default) | 8h |
| `AIRMVP1-305` | D19 | Tier C submission — cold email via Resend (warmup throttling) | 5h |
| `AIRMVP1-306` | D20 | Submission tracker UI + audit trail | 4h |
| `AIRMVP1-307` | D21 | Chaos test + bug-bash (10 real applies end-to-end) | 6h |

**Epic gate:** 10 real submissions logged across portal-API, Playwright, and email. Bill ≤ $15.

### Epic AIRMVP1-W4 — Recruiter CRM, Escalations, Landing (Week 4)

| Story ID | Day | Title | Estimate |
|---|---|---|---|
| `AIRMVP1-401` | D22 | Gmail API inbound polling + thread matching | 5h |
| `AIRMVP1-402` | D23 | Inbound classifier (Haiku) + auto-reply drafts + escalation logic | 6h |
| `AIRMVP1-403` | D24 | Notifications — SignalR in-app + daily email digest | 4h |
| `AIRMVP1-404` | D25 | Auto follow-up nudges (rate-limited, owner-approval default) | 4h |
| `AIRMVP1-405` | D26 | Marketing landing page + waitlist + SEO + Plausible | 5h |
| `AIRMVP1-406` | D27 | Stripe billing (test mode) + 14-day trial + customer portal | 5h |
| `AIRMVP1-407` | D28 | Owner UAT, bug bash, backlog grooming | 6h |

**Epic gate (MVP DONE):** ≥ 1 real recruiter reply received via the system. Second user can self-sign-up and reach matches. Bill ≤ $30.

> **Status:** W1–W4 stories (101–107, 201–207, 301–307, 401–407) built + merged.
> MVP gate verified via the manual UAT runbook (`docs/RUNBOOK-uat.md`) + the
> automated E2E pair (`PipelineE2ETests`, `RecruiterReplyE2ETests`). Open items
> rolled into `docs/BACKLOG.md` (notably AIR0002 CI/CD + AIR0005 a11y, deferred
> web screens, and the SignalR backplane).

---

## Phase 2 — Multi-user GA

**Branch prefix:** `AIRGA1-` · **Duration:** Weeks 5-8 (post-MVP).

| Epic | Story IDs | Goal |
|---|---|---|
| `AIRGA1-E1` Hardening | 110-series | Crash-free 99.5%, P95 < 300ms API, P99 < 1s |
| `AIRGA1-E2` Billing live | 120-series | Stripe production, MRR tracking, invoices |
| `AIRGA1-E3` Observability | 130-series | OpenTelemetry traces, Loki logs, Grafana dash |
| `AIRGA1-E4` A11y audit | 140-series | Manual screen-reader sweep, axe 0 violations, WCAG 2.2 AA cert |
| `AIRGA1-E5` First agency | 150-series | Onboard one real staffing agency, 3-5 consultants |

---

## Phase 3 — Scale & moat

**Branch prefix:** `AIRSCALE-` · **Duration:** Months 3-6.

| Epic | Goal |
|---|---|
| `AIRSCALE-E1` ATS library | Open-source per-ATS Playwright templates; community-maintained |
| `AIRSCALE-E2` Browser extension | Capture JD from any tab → tailor + apply |
| `AIRSCALE-E3` Agency white-label | Per-tenant brand, custom domain, role-based access |
| `AIRSCALE-E4` Resume video pitches | HeyGen / Synthesia API integration |
| `AIRSCALE-E5` Salary intel overlay | Levels.fyi / Glassdoor data per match |

---

## Cross-cutting epics (run alongside every phase)

| Epic ID | Title | Carries |
|---|---|---|
| `AIRX-E10` | Auth & multi-tenancy | identity, RBAC, tenant isolation tests |
| `AIRX-E11` | AI gateway & cost caps | `LlmGateway`, per-tenant budgets, audit log |
| `AIRX-E12` | Observability | structured logs, metrics, traces, alerts |
| `AIRX-E13` | Accessibility & ADA | axe/pa11y in CI, manual a11y checks, ACCESSIBILITY.md |
| `AIRX-E14` | Security & no-leaks | secret scanning, CodeQL, threat-model reviews, SECURITY.md |
| `AIRX-E15` | Privacy & data lifecycle | 90-day PII purge, GDPR/CCPA DSR endpoints |

---

## Tooling matrix

| Layer | Tool | Why | Cost |
|---|---|---|---|
| Language (backend) | C# 13 / .NET 10 LTS | Long-term support, fast, owner's strongest stack | $0 |
| Web framework | ASP.NET Core 10 Minimal API | Lightweight, OpenAPI-first | $0 |
| Background jobs | Hangfire + PostgreSQL storage | No Redis needed in v1, durable | $0 |
| ORM | EF Core 10 + Pgvector.EntityFrameworkCore | Vector search in DB | $0 |
| DB | Neon Postgres (serverless) | Generous free tier, branching | Free tier |
| Vector search | pgvector | In-DB, no separate service | $0 |
| Front-end | Next.js 15 (App Router) | SSR + ISR, great DX, Vercel free | $0 |
| UI lib | shadcn/ui + Tailwind | Accessible-by-default primitives | $0 |
| Browser automation | Microsoft Playwright (.NET) | Better stealth than Selenium, MSFT-backed | $0 |
| LLM | Anthropic Claude (Haiku 4.5 + Sonnet 4.6) | Cheap classifier + strong rewriter | < $20/mo |
| Embeddings | OpenAI `text-embedding-3-small` | 1536 dims fits pgvector | < $1/mo |
| Auth | ASP.NET Identity + JWT | No vendor lock-in | $0 |
| Email out | Resend | Free 3k/mo, easy DKIM | $0 |
| Email in | Gmail API per-tenant OAuth | No SMTP scraping | $0 |
| Storage | Azure Blob Storage | 5 GB free | $0 |
| Hosting | Azure Container Apps (consumption) + Vercel Hobby | Both scale-to-zero | $0 |
| Secrets | Azure Key Vault → GH Actions secrets in dev | Defense in depth | $0 |
| CI/CD | GitHub Actions | Free for public + private up to 2k min | $0 |
| Tracking | Jira Free | 10 users free | $0 |
| Analytics | Plausible (privacy-first) | Lightweight, GDPR-friendly | starts $9/mo |
| Errors | Sentry (free) → self-host if needed | Best DX | $0 |
| Logs/metrics | Serilog → Azure Monitor → later Grafana | Cheap start, expandable | $0 |
| Lint/format | dotnet format, ESLint, Prettier, Stylelint | CI-enforced | $0 |
| Security scan | gitleaks (pre-commit), CodeQL (CI), Dependabot | Three independent layers | $0 |
| Accessibility | axe-core, pa11y-ci, lighthouse-ci | Run on every PR | $0 |
| Testing | xUnit + Playwright Test + Vitest | Unit + E2E + component | $0 |
| Docs | mkdocs-material (later) | Public docs site | $0 |
| Diagrams | Mermaid + draw.io | Diagrams-as-code | $0 |
| Local dev | dotnet watch, pnpm dev, Azurite (Blob emulator) | Hot reload everywhere | $0 |
| Container | Docker + docker-compose for full local | Single `make dev` boot | $0 |

**Total at MVP load: < $30/mo, mostly Claude API + domains.**

---

## Definition of Done (universal — every story)

1. Branch follows naming convention (`AIR####-<slug>` or `AIRMVP{N}-<story-id>-<slug>`).
2. Every commit references the story ID in the footer (`Refs: AIRMVP1-XYZ`).
3. CI is green (build, test, lint, format, gitleaks, CodeQL, axe-core).
4. Unit tests cover happy path + 1 failure mode minimum.
5. For any user-facing change: keyboard-navigable, axe-core clean, contrast ≥ 4.5:1.
6. For any data-handling change: tenant isolation integration test still green.
7. For any AI call: `LlmGateway` used (never direct SDK); cost cap respected; prompt + response logged.
8. PR description references the Story ID, lists what changed and why, links to relevant ADR if architectural.
9. PR has at least one CODEOWNERS reviewer approval before merge.
10. `memory.md` updated if a decision was made or scope changed.
11. Branch deleted after merge.

---

## How to start a story (worked example)

```bash
# 1. From main, cut your branch
git checkout main && git pull
git checkout -b AIRMVP1-103-tenant-middleware

# 2. Do the work, commit in small atomic steps
git add apps/api/Aireq.Api/Middleware/TenantResolutionMiddleware.cs
git commit -m "feat(api): add tenant resolution middleware

Resolves tenant_id from JWT claims and stamps the ambient
IHttpContextAccessor for EF Core global query filters.

Refs: AIRMVP1-103"

# 3. ONE-COMMAND push + PR (never two steps)
./scripts/push-pr.sh
# or with an explicit title:
./scripts/push-pr.sh "AIRMVP1-103 tenant middleware"

# 4. After CI green + review, merge & delete branch
gh pr merge --squash --delete-branch
```

> **Project rule:** never run `git push` without opening the PR in the same command. Use `scripts/push-pr.sh` or the chained form `git push … && gh pr create … --web`. See [`AGENTS.md`](AGENTS.md#3-branch-and-commit-rules) for the full convention.
