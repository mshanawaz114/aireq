# ADR 0001 — Use .NET 10 LTS + Neon Postgres + Next.js 15 + Playwright

- **Status**: accepted
- **Date**: 2026-05-17
- **Deciders**: @mshanawaz114
- **Story / Driver**: AIR0006, originally locked in `memory.md` §5

## Context

We need a stack for an end-to-end SaaS Operations Copilot that ingests resumes, discovers jobs, tailors content, auto-submits via portals, and runs cold-email/recruiter-CRM workflows. The owner's primary language strength is C#; the target audience is staffing agencies (small budgets) so cost discipline matters. We also need browser automation, server-side LLM calls, and durable background jobs from day one. The MVP target is four weeks at < $30 / month.

## Decision

We will build Aireq on:

- **.NET 10 LTS** (`global.json` 10.0.100) for the API + background worker. ASP.NET Core 10 Minimal API.
- **Neon Postgres** (free tier) with the `pgvector` extension for both relational data and vector search.
- **Next.js 15** (App Router) on Vercel for the front-end.
- **Playwright (.NET)** for browser automation (job-board ingestion + auto-apply).
- **Hangfire** (Postgres-backed) for durable jobs — no Redis required in v1.
- **Claude API** (Haiku → Sonnet) for LLM calls, behind an `LlmGateway` abstraction so swapping providers is a localized change.

## Consequences

- **Positive**
  - Single-language backend reduces cognitive switching.
  - Neon's serverless model fits the bursty workload (workers idle most of the time) and offers branching DBs for dev/staging at zero cost.
  - `pgvector` keeps vector search in the same transactional boundary as relational data — no extra service to operate.
  - Playwright + .NET means we can reuse domain types between the API and the worker.
- **Negative**
  - .NET 10 is new (Nov 2025); some ecosystem packages still lag (e.g. EFCore.NamingConventions; we hand-roll snake-case naming in `AireqDbContext`).
  - Neon free tier is 0.5 GB — we have headroom but must purge old resume blobs aggressively.
  - Vercel free tier rate-limits builds; not a problem at MVP scale but worth noting.
- **Neutral**
  - Adds a Postgres dependency to the test suite; we mitigate by using EF InMemory for the cross-tenant isolation tests.

## Alternatives considered

1. **Node/TS backend + Postgres + Next.js** — would simplify type-sharing with the web app but loses C# strengths (LINQ, Playwright .NET, tooling familiarity). Lost.
2. **Python (FastAPI) + Celery + Postgres** — strong LLM ecosystem but introduces a second language with no offsetting upside for our workload. Lost.
3. **Supabase instead of Neon + custom backend** — faster start, but harder to express the auto-apply background-worker pipeline inside Supabase Edge Functions. Lost.
4. **Do nothing (off-the-shelf SaaS)** — none of LazyApply / Sonara / Simplify / JobCopilot / LoopCV target staffing agencies or offer a recruiter CRM (`memory.md` §16). Lost.

## Links

- Source of truth: `memory.md` §5–§9
- Related ADRs: 0002 (multi-tenancy), 0003 (LLM routing)
- Code touched: `apps/api/`, `apps/worker/`, `apps/web/`, `Aireq.sln`
