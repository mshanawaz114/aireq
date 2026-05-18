# 4-Week Build Plan — AI Operations Copilot

> Companion to `memory.md`. This is the **execution** document — day-by-day tasks, Jira epics, infra setup steps, and gate criteria for each week. Update `memory.md` for decisions and architecture; update this file when tasks shift.

**Working name:** TBD — Benchory / Reqloom / Aplora
**Stack:** .NET 10 LTS · Next.js 15 · Neon Postgres + pgvector · Playwright · Hangfire · Azure free tier · Resend · Claude API

---

## Pre-flight checklist (Day 0 — before week 1 starts)

Do these in order. Each is small. None take more than 30 minutes.

1. **Pick the name.** Buy the `.com` and the `.ai` on the same registrar.
2. **GitHub:** create the repo `<name>` (private to start). Add `.gitignore` (dotnet + node), `LICENSE` (MIT or proprietary), `README.md`, `memory.md`, `PLAN.md`.
3. **Jira:** create a Free workspace. Create a project of type **Scrum** named after the brand. Create 4 epics (one per week) and the 5 module epics from §Architecture. Board view = swimlanes by epic.
4. **Neon:** sign up, create a project `<name>-prod` and a branch `dev`. Capture the two connection strings into a `.env.local` template (commit the template, not values).
5. **Azure:** activate the free tier. Create a resource group `<name>-rg`. Don't provision compute yet — wait until day 5.
6. **Vercel:** sign up with your GitHub. We'll point it at the `apps/web` folder later.
7. **Anthropic API key:** create one. Set a $30 hard cap.
8. **Resend:** sign up, add the sending domain, set up SPF/DKIM/DMARC records at the registrar (Cloudflare DNS is fastest).
9. **Cursor / VS Code:** install the .NET 10 SDK preview and Node 22 LTS.

Gate: you can `dotnet --version` and see `10.x`, you can `node -v` and see `22.x`, and `git clone` your empty repo works.

---

## Week 1 — Foundation & Resume Intelligence

**Goal:** A logged-in user can upload a resume and see a parsed, embedded, queryable profile.

### Day 1 — repo skeleton
- `dotnet new sln`, scaffold `apps/api`, `apps/worker`, both ASP.NET Core 10 minimal API.
- `pnpm create next-app@latest apps/web --typescript --tailwind --app`.
- Set up GitHub Actions: `ci.yml` (build + test on PR), `deploy.yml` (stub for now).
- Wire `dotnet format`, `eslint`, `prettier` as pre-commit via husky.

### Day 2 — domain model & migrations
- Add `EntityFramework Core 10` + `Npgsql.EntityFrameworkCore.PostgreSQL` + `Pgvector.EntityFrameworkCore`.
- Implement entities from `memory.md` §6.
- First migration. Run against Neon `dev` branch.
- Seed script for one test tenant + one test consultant.

### Day 3 — auth & multi-tenant middleware
- ASP.NET Identity with email/password. JWT bearer tokens.
- `TenantResolutionMiddleware` reads `tenant_id` from JWT and stamps `IHttpContextAccessor`.
- EF Core global query filter: `entity.TenantId == _tenantContext.Current`.
- Integration test: user from tenant A cannot read tenant B's data. **This test must stay green for the life of the project.**

### Day 4 — resume upload & blob storage
- POST `/api/resumes` accepts multipart PDF/DOCX (max 5MB).
- Stream to Azure Blob (use `Azurite` locally for dev — no Azure cost yet).
- Enqueue `ParseResumeJob` via Hangfire.

### Day 5 — resume parsing (AI)
- Build `LlmGateway` service with a strict interface: `Task<T> ExtractAsync<T>(string prompt, string content)` returning typed JSON.
- Claude Haiku call extracts: `name`, `headline`, `emails`, `phones`, `skills[]`, `experiences[]`, `educations[]`, `certifications[]`.
- Store parsed JSON on `resumes.parsed_json`, populate `consultant_skills`.
- Generate embedding of full resume text → `resumes.embedding`.

### Day 6 — minimal web UI
- Next.js: login page, dashboard shell, "Upload resume" page, "My profile" page.
- Wire to API. Show parsed profile in editable form.
- Style with shadcn/ui. Mirror the prototype HTML the owner is reviewing.

### Day 7 — deploy & smoke
- Provision Azure Container App (consumption plan) for `api` and `worker`.
- Deploy `web` to Vercel.
- Connect Neon prod branch.
- End-to-end: sign up → upload resume → see parsed profile, live URL.

**Week 1 gate:** Working URL. Owner uploads his consultant's real resume. Profile renders correctly. Bill so far: $0.

---

## Week 2 — Job Discovery & Matching

**Goal:** ≥ 50 real, fresh, deduplicated openings matched and scored against the uploaded profile.

### Day 8 — Adzuna + USAJobs ingestion
- Hangfire recurring jobs: every 6h hit Adzuna API per consultant's preferred locations + roles.
- Every 12h hit USAJobs.
- Normalize into `jobs` table. Store raw payload in `raw_json` for forensics.

### Day 9 — Greenhouse / Lever / Ashby ingestion
- Maintain a `companies` table with `ats_provider` and `ats_handle`.
- Seed with 50 well-known consulting-friendly orgs (Accenture, Deloitte, Slalom, EPAM, mid-tier staffing).
- Per provider, hit `boards-api.greenhouse.io/v1/boards/<handle>/jobs`, `api.lever.co/v0/postings/<handle>`, `api.ashbyhq.com/posting-api/job-board/<handle>`.

### Day 10 — dedupe & freshness
- Implement content hash. Reject dupes within 30d.
- Mark jobs `is_active=false` after 2 consecutive scrapes without seeing them.
- Add `JobsCleanupJob` daily.

### Day 11 — embeddings & vector matching
- Compute embedding for every new job's JD.
- Matching query: cosine similarity between consultant resume embedding and job embedding, top-N candidates.
- Plus rule-based filters: location, work-auth, salary floor, exclusion keywords.

### Day 12 — match scoring & explanation
- For top 25 candidates, send `(resume, JD)` pair to Haiku and ask for a 0-100 match score with 3-bullet rationale.
- Persist to `matches.score` and `matches.reasoning_json`.

### Day 13 — Matches UI
- `/matches` page: sortable list, score badge, why-it-matches tooltip, source badge, "Tailor & apply" button.
- Filters: source, score, location, posted-within.

### Day 14 — polish & metrics
- Add a tiny analytics table `events` (event_name, tenant_id, payload, at).
- Track: `resume.parsed`, `job.ingested`, `match.scored`, `match.viewed`.
- Lightweight admin dashboard for owner: jobs/day, matches/day, cost/day.

**Week 2 gate:** Owner sees ≥ 50 real matches for his consultant, with scores and explanations. Bill so far: < $5.

---

## Week 3 — ATS Optimization & Auto-Apply

**Goal:** One click takes a match through: tailor → submit → log. Both via API (Tier A) and Playwright (Tier B).

### Day 15 — ATS keyword extractor
- Parse JD into structured `required_skills`, `nice_to_have`, `keywords[]`.
- Diff against the consultant's `skills` and `experiences`.
- Surface a "Missing keywords" panel in the UI before tailoring.

### Day 16 — resume rewriter (AI)
- Sonnet call: input = (master resume JSON, target JD, extracted keywords). Output = tailored resume JSON with the same structure.
- Render the tailored resume to PDF via a Razor template + Puppeteer-Sharp (or use a DocX template then convert).
- Store under `tailored_resumes`. Compute an ATS-score by counting keyword hits.

### Day 17 — Tier A submission: Greenhouse + Lever APIs
- Greenhouse: `POST https://boards-api.greenhouse.io/v1/boards/<handle>/jobs/<id>/applications` with the tailored PDF.
- Lever: `POST https://api.lever.co/v1/postings/<handle>/<id>` (different shape).
- Record `submissions` row. Update `matches.status = 'submitted'`.

### Day 18 — Tier B submission: Playwright templates
- Build one Playwright "template" per ATS family (Greenhouse-hosted UI, Lever-hosted UI, Workday, iCIMS).
- Each template = a function `apply(page, jobUrl, profile, tailoredResumePath)`.
- Run in the `worker` container. Use stealth plugins. Random human-ish delays.
- **Critical safety:** every apply has a `dry_run` toggle. In v1, default to `dry_run=true` and require explicit owner confirmation in UI.

### Day 19 — Tier C submission: cold email
- For matches without a portal, find recruiter email (Hunter.io free tier OR LinkedIn lookup OR `careers@<domain>`).
- Sonnet drafts a cold email referencing 2 specific JD points + 2 specific resume bullets.
- Sends via Resend. Threading captured.
- Throttle: max 30 new outreach emails/day from a fresh domain (warmup).

### Day 20 — submission tracker UI
- `/submissions` page: timeline view per match. Shows: tailored PDF preview, channel, sent-at, response.
- Re-apply / un-submit toggles disabled (these are real submissions).
- Owner can audit AI-generated email content **before** send if `require_approval=true`.

### Day 21 — chaos test
- Spin up a fake ATS using a Greenhouse demo board. Run 10 real apply cycles.
- Owner runs it against 5 real jobs end-to-end.
- Fix the top 3 bugs.

**Week 3 gate:** 10 successful real submissions logged across portal-API, Playwright, and email channels. Bill so far: < $15.

---

## Week 4 — Recruiter CRM, Escalations, Landing Page, Polish

**Goal:** When a recruiter replies, the right person gets the right ping at the right time. Public landing page live.

### Day 22 — inbound email ingestion
- Gmail API OAuth flow for owner (later: per-tenant).
- Poll inbox every 5 min via Hangfire recurring job.
- Match inbound email to `recruiter_threads` by sender + thread-id.

### Day 23 — inbound classification & follow-up agent
- Haiku classifies each inbound as: `interview_request | rejection | info_request | scheduling | other`.
- For `info_request`: AI drafts a reply, queued for owner approval.
- For `scheduling`: pull free slots from Google Calendar, draft reply.
- For `interview_request` or `rejection`: **escalate** (do not auto-reply).

### Day 24 — notifications
- In-app real-time via SignalR.
- Email digest to owner: 8am daily — "3 new interviews, 7 new replies, 2 awaiting your approval".
- Mobile-friendly UI for the owner to act on his phone.

### Day 25 — auto follow-up
- For submissions with no reply after 5 business days → AI-drafted polite nudge → owner approval (optional auto-send after 2 nudges).

### Day 26 — landing page + waitlist
- Marketing site at root. Hero, 3-step explainer, pricing teaser, waitlist form (writes to `waitlist` table).
- SEO: title, OG image, sitemap.
- Plausible Analytics free tier.

### Day 27 — billing scaffolding
- Stripe integration (test mode). Pricing per `memory.md` §13.
- Free 14-day trial.
- Stripe Customer Portal for self-serve management.

### Day 28 — owner UAT + bug bash
- Owner runs the system on his own consultant for the full day.
- Triage bugs. Fix top 5 P1s. Document P2s in `docs/BACKLOG.md`.

**Week 4 gate (MVP done):**
- ≥ 1 real submission has resulted in an actual recruiter reply.
- Owner has used the inbox + escalation flow in production for himself.
- A second user can sign up via the public site, upload a resume, and reach matches without owner help.

---

## Jira Layout

**Project key:** suggested `AOC` (AI Operations Copilot) or after the brand.

**Issue types:** Epic, Story, Task, Bug, Spike.

**Epics (create on day 0):**
1. `AOC-E1` Foundations (week 1)
2. `AOC-E2` Job Discovery (week 2)
3. `AOC-E3` Tailor & Apply (week 3)
4. `AOC-E4` CRM & Polish (week 4)
5. `AOC-E10` Cross-cutting: Auth & Multi-tenancy
6. `AOC-E11` Cross-cutting: AI Gateway & Cost Caps
7. `AOC-E12` Cross-cutting: Observability & Logging

Each day in this plan = 1 Story (or small cluster) under the appropriate week epic.

**Boards:**
- **Now / Next / Later** board for the owner.
- **Sprint** board with 1-week sprints aligned to the weeks above.

**Definition of Done (per story):** code merged, integration test green, deployed to dev, demoable.

---

## What "later" looks like (post-MVP backlog)

Captured so we don't forget but DO NOT build in 4 weeks:
- Chrome extension that captures a JD from any page and triggers tailor+apply.
- Auto-warmup of the sending domain (network of seed inboxes).
- Interview scheduling autopilot (calendar slots, Zoom link generation).
- Slack / Discord / WhatsApp notifications.
- Per-agency white-label.
- A/B-test different tailored resume variants and learn which gets replies.
- Resume video pitch generation (HeyGen / Synthesia API).
- Salary intelligence overlay per match (Levels.fyi / Glassdoor data).
- Open-source the Playwright ATS templates so the community keeps them current.

---

## Decision quick-reference

| Decision | Value | Source |
|---|---|---|
| .NET version | 10 LTS | owner explicit |
| Multi-tenant | yes, from day 1 | owner explicit |
| Browser automation | full Playwright, tiered approach | owner explicit |
| LLM provider | Claude (Haiku+Sonnet), swappable | cheapest-first heuristic |
| Job sources | Adzuna + USAJobs + GH/Lever/Ashby + RSS + targeted Playwright | best-results heuristic |
| Hosting | Azure free → Container Apps when traffic grows | free-tier rule |
| Database | Neon Postgres + pgvector | owner explicit |
| Frontend host | Vercel | free + lowest friction |
| Email | Resend | free tier 3k/mo |
| CI/CD | GitHub Actions | free for private repos |
| Tracking | Jira free | owner explicit |
| Memory | `memory.md` (this repo) | owner explicit |
