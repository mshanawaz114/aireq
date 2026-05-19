# CLAUDE.md — Codebase context for AI agents

> **Auto-loaded by Claude Code, Cowork, and any Claude Agent SDK app when this
> repo is the working directory. Keep terse — pointers to fuller docs, not a
> re-statement of them.**

## What this is

**Aireq** ("AI-rek") — AI Operations Copilot for staffing agencies and
independent consultants. Multi-tenant SaaS from day 1. End-to-end loop:
resume → discover real openings → tailor per JD → auto-submit → follow-up →
escalate to human only when needed.

## Read these first

| File | When to read |
|---|---|
| [`memory.md`](memory.md) | **Source of truth** for every decision, requirement, name, and conversation summary. Read this before doing anything. |
| [`PLAN.md`](PLAN.md) | Source of truth for execution — phases, epics, stories with stable IDs. Read this to find your next task. |
| [`AGENTS.md`](AGENTS.md) | Branch/commit/PR conventions. Required before any git work. |
| [`README.md`](README.md) | Local-dev quickstart (Make targets, env setup, ports). |

## Stack at a glance

.NET 10 LTS · ASP.NET Core 10 Minimal API · EF Core 10 + Pgvector ·
Hangfire (Postgres-backed) · Next.js 15 (App Router) · Tailwind + shadcn/ui ·
Playwright (.NET) · Anthropic Claude (Haiku for parse/classify, Sonnet for
rewrite) · Neon Postgres · Azure free tier · Vercel Hobby.

## Branch + commit conventions (short form — full in AGENTS.md)

- Branches: `AIR####-<slug>` (foundation) or `AIRMVP{N}-<story-id>-<slug>` (MVP).
- Commits: Conventional Commits — `<type>(<scope>): <subject>` with `Refs: <story-id>` in body.
- Push + PR in one chain — `./scripts/push-pr.sh`. Never `git push` alone.

## Per-story workflow (one branch, one PR, one merge)

```bash
git checkout main && git pull
git checkout -b <branch>
# work…
git add -A && git commit -m "..."
./scripts/push-pr.sh
```

## Available repo-local skills

Drop into `.claude/skills/` — auto-discovered by Claude Code / Cowork:

| Skill | Use when |
|---|---|
| `aireq-verify` | User wants a full pre-commit verification (build + tests + typecheck + lint + web build). |
| `aireq-new-story` | User wants to start a new story branch with the right name and base. |

Add more skills by creating `.claude/skills/<name>/SKILL.md` with YAML
frontmatter (`name`, `description`) and a clear instruction body.

## Hard guardrails

1. **Never bypass `LlmGateway`** — direct SDK calls escape cost caps and audit logging.
2. **Every query through EF Core respects tenant filters** unless `IgnoreQueryFilters()` is explicit and justified.
3. **No secrets in commits** — `gitleaks` pre-commit + GitHub push protection are both on.
4. **No auto-merging without verification** — `make verify` must pass locally; CI must be green; reviewer must approve.
