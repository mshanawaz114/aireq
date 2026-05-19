# ADR 0002 — Multi-tenancy via EF Core global query filters, not RLS

- **Status**: accepted
- **Date**: 2026-05-18
- **Deciders**: @mshanawaz114
- **Story / Driver**: AIRMVP1-103

## Context

Aireq is multi-tenant from day one: every entity that isn't a global lookup (Skill, Job) belongs to exactly one Tenant. A bug that returns Tenant A's data to Tenant B is a P0 incident.

Two mainstream Postgres patterns:

- **Application-layer scoping** — every query is filtered by `tenant_id` in code (LINQ predicates or EF Core query filters).
- **Database-layer Row Level Security (RLS)** — Postgres enforces a `tenant_id = current_setting('app.tenant_id')` predicate transparently on every row.

RLS is the textbook "defense in depth" answer. But it has friction: every connection has to `SET LOCAL app.tenant_id`, EF Core support is awkward at MVP scale, and any code path that needs to cross tenants (admin, signup, background jobs) needs explicit `SECURITY DEFINER` carve-outs.

## Decision

For MVP and Phase 1 GA, we will enforce tenant isolation with **EF Core `HasQueryFilter` global filters** scoped to `ITenantContext.TenantId`, backed by a **mandatory cross-tenant integration test** (`tests/Aireq.Api.Tests/Tenancy/QueryFilterTests.cs`) that runs in CI on every PR.

Endpoints that legitimately need to cross tenants (signup, login, admin reads) call `.IgnoreQueryFilters()` explicitly. The `ITenantContext` interface is request-scoped and sourced from JWT claims via `HttpTenantContext`.

We will revisit and **add RLS as a second layer** when one of these triggers fires: (a) first paying customer, (b) first reported cross-tenant data leak (even self-reported), (c) any compliance certification ask (SOC2, ISO 27001, HIPAA).

## Consequences

- **Positive**
  - Fastest path to a working multi-tenant API; no per-connection session state.
  - Filter behaviour is unit-testable against EF InMemory — runs in CI in milliseconds.
  - Crossing tenants is grep-able (`IgnoreQueryFilters`) which makes audit easy.
- **Negative**
  - A raw SQL escape hatch (`db.Database.ExecuteSqlRawAsync`) bypasses the filter. We mitigate by code review on `/Data/` paths via CODEOWNERS.
  - A future contributor who forgets to add a filter on a new entity creates a leak. We mitigate by the `QueryFilterTests` (which seeds every entity) and a lint rule to be added in AIRGA1-* hardening.
- **Neutral**
  - Postgres `tenant_id` columns are normal indexed columns; no schema oddities.

## Alternatives considered

1. **RLS only** — most secure, but operational overhead is high for MVP velocity. Deferred to Phase 2 trigger.
2. **Schema-per-tenant** — clean isolation but explodes migration cost and Neon storage. Rejected.
3. **Database-per-tenant** — operationally heaviest; only justifiable at enterprise scale. Rejected.

## Links

- Source code: `apps/api/Aireq.Api/Data/AireqDbContext.cs`, `apps/api/Aireq.Api/Auth/HttpTenantContext.cs`
- Test: `tests/Aireq.Api.Tests/Tenancy/QueryFilterTests.cs`
- Threat model: `SECURITY.md` (P0 row)
