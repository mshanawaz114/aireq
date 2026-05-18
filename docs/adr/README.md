# Architecture Decision Records (ADRs)

This directory captures decisions that change the *shape* of Aireq — stack choices, isolation models, integration boundaries, anything an engineer would want a written rationale for six months from now.

## How to add one

1. Copy `0000-template.md` to `NNNN-short-title.md` using the next available number.
2. Fill it out — context, decision, consequences, alternatives, links.
3. Open a PR with the ADR. Title: `docs: ADR NNNN <title>`.
4. Reference the ADR from the relevant Story in `PLAN.md` and from the code it constrains.

## How ADRs become obsolete

An ADR is **never deleted**, only marked `superseded by ADR-XXXX` and linked forward. The history is the artefact.

## Index

| # | Title | Status | Driver |
|---|---|---|---|
| [0001](./0001-net10-postgres-nextjs-stack.md) | .NET 10 LTS + Neon Postgres + Next.js 15 + Playwright | accepted | AIR0006 |
| [0002](./0002-multitenancy-via-query-filters.md) | Multi-tenancy via EF Core query filters, not RLS | accepted | AIRMVP1-103 |
| [0003](./0003-llm-routing-claude-haiku-sonnet.md) | `LlmGateway` + Claude Haiku → Sonnet routing | accepted | AIRMVP1-105 |

## What does NOT belong here

- Day-to-day implementation details (those go in code comments).
- Personnel / vendor decisions (those go in `memory.md`).
- Prompt copy or business-logic constants (those go in code or `memory.md`).
