# memory.md — Persistent Project Memory

> **Purpose:** Single source of truth for this project. Any AI, agent, or human picking this up should read this file first. Append new decisions, requirements, and conversation summaries at the bottom — never overwrite older entries unless explicitly marked stale.
>
> **Update rule:** After every meaningful conversation turn (decision made, requirement added, scope change, blocker found), append a dated entry to the **Conversation Log** section and reflect the change in the appropriate structured section above it. Never delete history.

---

## 0. Quick Project Card (read this first)

| Field | Value |
|---|---|
| **Working name** | **Aireq** (pronounced "AI-rek", two syllables — "AI" + "req"). Locked on 2026-05-17. |
| **Tagline** | AI Operations Copilot for staffing agencies and consultants |
| **Owner** | shahnawaz (mshanawaztech@gmail.com) |
| **Started** | 2026-05-17 |
| **Target MVP ship** | 4 weeks (≈ 2026-06-14) |
| **Stack (locked)** | .NET 10 LTS · Neon Postgres + pgvector · Next.js · Playwright · Azure free tier · GitHub · Jira |
| **LLM (start)** | Cheapest / free-tier first. Default: Claude Haiku for extraction, escalate to Sonnet for resume rewriting. Swap to gpt-4o-mini if Azure credits are healthier. |
| **Repo** | Not yet created — will be `github.com/<owner>/<chosen-name>` |
| **Jira** | Not yet created |

---

## 1. The Problem (one paragraph)

The staffing / IT-consulting market is operationally broken. Job boards (Indeed, Dice, ZipRecruiter, LinkedIn) show stale and duplicated postings, ATS filters reject candidates on keyword mismatch alone, and recruiters operate in a black hole — most applications get no reply for two weeks. Independent consultants and small staffing firms waste 60–80% of their day on submissions and follow-ups instead of relationship work. There is no end-to-end agent that pulls real openings, tailors the resume to each role, submits on behalf of the consultant, follows up, and only escalates when a human is actually needed.

## 2. The Product (one paragraph)

A SaaS "AI Operations Copilot" that ingests a consultant's resume + LinkedIn + preferences, builds an enriched skill profile, continuously discovers real openings across multiple sources, scores and de-duplicates them, dynamically rewrites the resume per role to pass ATS, auto-submits via Playwright + official APIs where allowed, sends and tracks cold follow-up email, maintains a recruiter CRM, and only pings the human owner when an interview is requested or a recruiter replies meaningfully. The product is multi-tenant from day one (single-consultant accounts and agency accounts).

## 3. Candidate Names (verified unique on 2026-05-17)

All three were web-searched and do NOT collide with an existing SaaS brand. Domain availability on `.com` / `.ai` must still be verified at the registrar before purchase.

1. **Benchory** — "bench" (staffing industry term for consultants currently between projects) + "-ory" suffix. Industry-specific, hard to mistake.
2. **Reqloom** — "req" (job requisition) + "loom" (weaves submissions, follow-ups and recruiters into one fabric). Modern AI-SaaS feel.
3. **Aplora** — "apply" + "-ora" suffix. Direct nod to the auto-apply core feature; easy to say.

**Rejected:** Reqora (already a SaaS — Amazon returns), Pilotry (aviation software in Germany).

**Recommendation:** Benchory if you want to lean into staffing-industry credibility; Reqloom if you want a broader AI-platform feel; Aplora if you want the most consumer-friendly read.

## 4. Goals & Non-Goals

### Goals (MVP, 4 weeks)
- End-to-end demo: upload resume → auto-discover jobs → AI-tailor resume per job → auto-submit (Playwright + APIs) → cold email → follow-up → escalate when human needed.
- SaaS architecture (multi-tenant) from day 1, even if only 1 user uses it.
- Free-tier infra only.
- Public landing page + waitlist.
- Working prototype the owner can run for himself first.

### Non-Goals (explicitly deferred)
- Mobile apps.
- Browser extension.
- Marketplace / two-sided network.
- Phone-call automation.
- Video interview bots.
- Indeed/LinkedIn full ToS-defying scraping at scale (we use APIs + RSS + targeted Playwright on portals that allow it).

## 5. Architecture (locked, v1)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  FRONTEND  ── Next.js 15 (App Router) on Vercel free tier                │
│  ├─ /app    dashboard, resume upload, profile, matches, submissions, CRM │
│  ├─ /(marketing) landing page + waitlist                                 │
│  └─ Tailwind + shadcn/ui                                                 │
└──────────────────────────────┬──────────────────────────────────────────┘
                               │ HTTPS / JSON
┌──────────────────────────────▼──────────────────────────────────────────┐
│  API   ── ASP.NET Core 10 (LTS) Minimal API                              │
│  ├─ Auth: ASP.NET Identity + JWT (later: Auth0 free tier if needed)      │
│  ├─ Modules:                                                             │
│  │   • Profile / Resume                                                  │
│  │   • Job Discovery                                                     │
│  │   • Match & ATS Rewrite                                               │
│  │   • Submission Orchestrator                                           │
│  │   • Email / Recruiter CRM                                             │
│  │   • Notification & Escalation                                         │
│  ├─ Hangfire (Postgres-backed) for background jobs — no Redis needed v1  │
│  └─ Hosted on Azure App Service F1 (free) OR Azure Container Apps        │
│      consumption plan (free monthly grant)                               │
└────────┬──────────────────────────────┬──────────────────────────┬──────┘
         │                              │                          │
┌────────▼────────┐         ┌───────────▼──────────┐   ┌──────────▼──────┐
│  Neon Postgres  │         │  Playwright Worker   │   │  LLM Providers   │
│  + pgvector     │         │  (separate container)│   │  • Claude (start)│
│  (free tier)    │         │  Auto-apply, scrape  │   │  • Azure OpenAI  │
│  Multi-tenant   │         │  career pages, fill  │   │  • Embeddings    │
│  RLS-style      │         │  forms. Queued via   │   │    via pgvector  │
│  partitioning   │         │  Hangfire.           │   │    (in DB)       │
└─────────────────┘         └──────────────────────┘   └──────────────────┘
                                       │
                              ┌────────▼────────┐
                              │  Email Sending  │
                              │  Resend free    │
                              │  (3k/mo free)   │
                              │  + Gmail API    │
                              │  for read/reply │
                              └─────────────────┘
```

### Service decomposition
- **api**: ASP.NET Core 10 web app — REST + SignalR for live updates.
- **worker**: separate process (same repo) hosting Hangfire server + Playwright. Container Apps scale-to-zero.
- **web**: Next.js front-end.
- **shared**: domain models, contracts.

### Repo layout
```
/<chosen-name>/
├── apps/
│   ├── api/          (ASP.NET Core 10)
│   ├── worker/       (ASP.NET Core 10 Hangfire host + Playwright)
│   └── web/          (Next.js 15)
├── packages/
│   └── shared/       (shared TypeScript types generated from OpenAPI)
├── infra/
│   ├── bicep/        (Azure resources)
│   └── github/       (Actions workflows)
├── docs/
│   ├── PLAN.md
│   ├── ARCHITECTURE.md
│   └── RUNBOOK.md
├── memory.md         (this file)
└── README.md
```

## 6. Data Model (initial)

Tables (Postgres, `public` schema, soft-delete via `deleted_at`):

- `tenants` (id, name, plan, created_at)
- `users` (id, tenant_id, email, role, ...)
- `consultants` (id, tenant_id, full_name, headline, location, work_auth, rate_target, ...)
- `resumes` (id, consultant_id, version, source_blob_url, parsed_json, embedding `vector(1536)`)
- `skills` (id, name) + `consultant_skills` (consultant_id, skill_id, years, evidence)
- `jobs` (id, source, source_external_id, title, company, location, posted_at, expires_at, raw_json, embedding `vector(1536)`, is_active)
- `matches` (id, consultant_id, job_id, score, reasoning_json, status [new|reviewing|tailored|submitted|reply|interview|rejected])
- `tailored_resumes` (id, match_id, blob_url, ats_score, diff_json)
- `submissions` (id, match_id, channel [portal|email|api], submitted_at, response_status, response_payload_json)
- `recruiter_threads` (id, match_id, recruiter_email, last_inbound_at, sentiment, requires_human)
- `messages` (id, thread_id, direction, body, sent_at, generated_by_ai)
- `escalations` (id, match_id, reason, created_at, resolved_at)

Multi-tenant isolation: every query goes through a tenant filter middleware. Move to RLS later.

## 7. Job Discovery Strategy (week 1-2)

Stacked, cheapest-first:

1. **Adzuna API** (free tier — 1k calls/mo). Aggregates from many boards. Best legal coverage.
2. **USAJobs.gov API** (free, unlimited) — federal openings.
3. **Greenhouse / Lever / Ashby / Workable public job board JSON endpoints** — many companies expose `/boards/<co>/jobs.json`. Legal, structured, fresh.
4. **RSS feeds** from LinkedIn job alerts, Indeed saved-search RSS (where still supported).
5. **Targeted Playwright** for company career pages that don't expose APIs — scheduled, polite (1 req/min/site), respecting robots.txt.

De-dupe pipeline: hash of `(company, title, location, jd_first_500_chars)` → drop dupes within 30-day window. Freshness check: re-scrape every 72h, mark `is_active=false` if no longer found.

## 8. Auto-Apply Strategy (week 3)

User chose **Full Playwright auto-apply**. Realistic phasing:

- **Tier A — APIs (zero risk):** Greenhouse, Lever, Ashby all expose submit endpoints to their tenants. Many companies use them. Submit via API where the company has it.
- **Tier B — Playwright on ATS templates (low risk):** Greenhouse-hosted, Lever-hosted, Workday-hosted, iCIMS portals have predictable DOMs. Write per-ATS templates, not per-company. ~80% portal coverage with 4-5 templates.
- **Tier C — Generic resume drop + email (medium risk):** When portal is unknown, fall back to email apply to `careers@…` or to the recruiter contact harvested from JD.
- **Tier D — Manual fallback (always):** If automation fails, surface in dashboard with "1-click open in browser" + pre-filled draft.

Anti-bot reality: Indeed and LinkedIn actively block. Treat them as **discovery sources, not submission targets** in v1.

## 9. AI / LLM Strategy

| Task | Model (start) | Rationale |
|---|---|---|
| Resume parsing (PDF → JSON) | Claude Haiku 4.5 | Cheap, strong structured output |
| Skill extraction | Claude Haiku 4.5 | Same |
| JD parsing | Claude Haiku 4.5 | Same |
| Embeddings | OpenAI `text-embedding-3-small` OR Voyage AI free tier | 1536 dims fits pgvector |
| Resume rewriting per JD | Claude Sonnet 4.6 | Quality matters here |
| Cold email drafting | Claude Sonnet 4.6 | Same |
| Inbound classification (recruiter reply: "interview?" / "rejection" / "info-request") | Claude Haiku 4.5 | Cheap, frequent |

Token budget guardrails per consultant per month: 250k input / 50k output Haiku, 80k input / 20k output Sonnet. Hard cap enforced in `LlmGateway` service.

## 10. Infra & Cost (free tier all the way)

| Service | Tier | $ |
|---|---|---|
| Neon Postgres | Free (0.5 GB) | $0 |
| Azure App Service / Container Apps | Free F1 / consumption grant | $0 |
| Azure Blob Storage (resumes) | Free 5 GB | $0 |
| Vercel (Next.js) | Hobby | $0 |
| Resend email | Free 3k/mo | $0 |
| GitHub | Free | $0 |
| Jira | Free 10 users | $0 |
| Claude API | Pay-as-you-go | < $20/mo at MVP load |
| Domain (.com or .ai) | One-time | ~$12–$80/yr |
| **Total month 1** | | **~$20–30** |

Scale-up triggers (when to upgrade and to what) live in `docs/RUNBOOK.md`.

## 11. Security & Compliance (must-haves before any paid customer)

- Secrets via Azure Key Vault (or GitHub Actions secrets in earliest dev).
- All resumes encrypted at rest (Azure Blob default + customer-side encryption later).
- Per-tenant data isolation — every query filtered by `tenant_id`; integration test enforces this.
- Audit log of every AI-generated outbound email (who, when, what model, full prompt + response).
- PII handling notice on landing page; do not retain raw resume content beyond 90 days for free-tier users.
- Email deliverability: SPF + DKIM + DMARC on the sending domain from day 1 (most cold-email fails ignore this).

## 12. Open Questions / TBD

- [ ] Final brand name (pick from §3).
- [ ] Domain registrar (Namecheap vs Cloudflare Registrar — Cloudflare is at-cost).
- [ ] Sending domain strategy (own domain vs subdomain like `mail.<brand>.com`).
- [ ] Whether to gate auto-apply behind explicit per-job "approve & send" toggle in v1 (recommended for safety).
- [ ] Pricing tier names + numbers (initial draft in §13).

## 13. Pricing (draft, week 4)

- **Solo** — $39/mo. 1 consultant, 50 auto-applies/mo, 200 AI tailors/mo.
- **Pro** — $99/mo. 1 consultant, 200 auto-applies, unlimited tailors, recruiter CRM.
- **Agency** — $399/mo. Up to 10 consultants, all features, shared recruiter inbox.
- **Enterprise** — talk to us.

Free trial: 14 days, no credit card.

## 14. Risks & Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Indeed / LinkedIn IP-block | High | Medium | Use them as discovery only, not submission. Rotate residential proxies later if needed. |
| LLM cost spike | Medium | Medium | Token budget cap per tenant; cheapest model by default. |
| Submissions sent to wrong job | Medium | High | Require user "approve next 10 applies" toggle in v1; one-click revoke. |
| Email domain marked as spam | High | High | SPF/DKIM/DMARC, warmup schedule, < 50 sends/day from new domain for first 2 weeks. |
| Playwright per-portal breakage | High | Low-Med | Template-per-ATS not per-company; daily smoke test against fixtures. |
| ToS lawsuits from boards | Low | High | API-first; respect robots.txt; consult lawyer before paid launch. |

## 15. Definitions of Done

**MVP (week 4) is "done" when:**
1. Owner can sign up at the public URL, upload his consultant's resume, and see ≥ 50 matched real openings within 1 hour.
2. Owner clicks "Apply & Tailor" on one match and the system: rewrites the resume, submits via Playwright/API, logs the submission, and emails the owner the confirmation.
3. When a recruiter replies (test inbox), the system classifies it and pings the owner.
4. All of the above runs on free-tier infra and costs < $30/month total.

---

## 16. Conversation Log (append-only)

### 2026-05-17 — kickoff session

**Decisions made:**
- Stack: .NET 10 LTS (not .NET 8). Neon Postgres free. Azure free. GitHub. Jira coming.
- Audience: SaaS product (multi-tenant from day 1) but first end-to-end goal is owner's own consultant.
- Automation depth: Full Playwright auto-apply requested. Implementing phased Tier A→D approach to deliver in 4 weeks.
- LLM: Cheapest first. Claude Haiku for parsing/classification, Sonnet for rewriting; can swap to Azure OpenAI mini.
- Job sources: "Whichever gives best results" → stacked Adzuna + USAJobs + Greenhouse/Lever/Ashby + RSS + targeted Playwright.
- Timeline: 4 weeks max.

**Open items the owner still needs to do:**
- Pick a final name from Benchory / Reqloom / Aplora.
- Create the GitHub repo.
- Create the Jira project.
- Buy the domain.

**Owner's quality-of-life requirements:**
- This memory.md must be the durable record so other AI tools / agents / sessions can resume without re-asking.
- Owner wants to see a prototype UI before any code is written → being built alongside this plan.

**Names verified unique on 2026-05-17 via web search:**
- Benchory — clean.
- Reqloom — clean.
- Aplora — clean.
- Reqora — TAKEN (Amazon returns SaaS). Rejected.
- Pilotry — TAKEN (aviation software, Germany). Rejected.

### 2026-05-17 (cont.) — competitive scan & name discussion

**Owner asked about:** https://ar.kaispe.com/ (Kaispe AutoRecruit) — concern that it overlaps.

**Verdict — NOT a competitor; opposite side of the same market.**

| | Kaispe AutoRecruit | This project |
|---|---|---|
| Side | Employer / HR (inbound) | Consultant / agency (outbound) |
| Buyer | HR depts, min. 5 seats | Consultants & staffing firms |
| Stack | MS Power Platform / Dynamics 365 / Power Apps | .NET 10 + Next.js + Playwright + AI |
| Distribution | Azure Marketplace / AppSource | Direct SaaS |
| Use of each other | Kaispe customers are ATS endpoints our Playwright workers will submit INTO | — |

Kaispe's existence is a positive market signal, not a threat.

**Actual consumer-side competitors worth tracking (none are agency-focused, none use serious per-JD rewriting + recruiter CRM):**
- LazyApply
- Sonara
- Simplify.jobs
- JobCopilot
- LoopCV

The agency tier is wide open — that is our wedge.

**Name discussion:**
- Owner proposed `autoaiconsultant`. Rejected because: 17 chars, three glued generic words, conceptually too close to Kaispe's "AutoRecruit", SEO trap, hard to say, not trademarkable.
- `Autoreq` was considered but **rejected** — phonetically identical to **AutoRek** (autorek.com), a 30-year-old, $17M-revenue financial reconciliation SaaS. Brand collision risk too high.

### Name LOCKED — Aireq

- **Brand:** **Aireq**
- **Pronunciation:** "AI-rek" (two syllables: "AI" as in artificial intelligence + "req" as in requisition).
- **Why this name:** Owner first chose "Autoaireq" then switched to the shorter form on 2026-05-17 after registrar check confirmed `aireq` domains were available. Rationale: 5 chars, easier to pronounce, easier to type, more trademarkable, AI automation is implied by the name itself so the "auto" prefix is redundant.
- **Web-search verification on 2026-05-17:** zero hits across SaaS / software / brand searches. Clean.
- **Pronunciation strategy:** display `Aireq (AI-rek)` once on landing page and in the about page first sentence. Standardize internal speech to "AI-rek" (not "air-eek").
- **Domains to buy:** `aireq.com` (primary), `aireq.ai` (secondary), `aireq.io` (defensive). Owner confirmed `.com` available on 2026-05-17.
- **Trademark check before any paid customer:** USPTO TESS search for "aireq" — should come back empty given zero web hits.
- **Jira project key:** `AIR`.
- **GitHub repo:** `github.com/<owner>/aireq`.
- **.NET namespace root:** `Aireq.*` (e.g. `Aireq.Api`, `Aireq.Worker`, `Aireq.Shared`).
- **Previous candidate "Autoaireq" — superseded** (recorded for history): 8 chars, ambiguous pronunciation, redundant "auto" prefix. Switched away before any artifact was committed to git, so no rename cost incurred.
