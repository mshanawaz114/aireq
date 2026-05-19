---
name: aireq-verify
description: Run the full Aireq local verification suite — dotnet build, dotnet test, web typecheck, web lint, web build. Use whenever the user wants to confirm a change is safe to commit, or after writing new code, or before pushing a branch. Triggers — "verify", "run tests", "is this OK to commit", "smoke test", "before I push", "make sure everything builds".
---

# aireq-verify

Runs every check that CI will run, but locally. Produces a single
green/red verdict the user can act on.

## What to run

From the repo root, in order, **stopping at the first failure**:

```bash
# 1. .NET — solution build (api + worker + shared + tests)
dotnet build Aireq.sln -c Debug

# 2. .NET — unit + integration tests
dotnet test tests/Aireq.Api.Tests/Aireq.Api.Tests.csproj -c Debug \
  --logger "console;verbosity=minimal"

# 3. Web — type-check (catches TS errors that lint may miss)
( cd apps/web && pnpm typecheck )

# 4. Web — lint (next + eslint config)
( cd apps/web && pnpm lint )

# 5. Web — production build (catches Server Component issues, missing env, etc.)
( cd apps/web && pnpm build )
```

## Reporting

- **All five green** → say "verify: 5/5 green, safe to commit" and stop.
- **Any failure** → paste the failing tool's last 30 lines of output, identify
  the file + line that broke, and **propose a fix as a diff**. Do not
  silently continue to the next step.

## Common failures + first-look fixes

| Symptom | First guess |
|---|---|
| `Pgvector.Vector` mapping error in tests | Missing `b.Ignore(x => x.Embedding)` in the non-Npgsql branch of `OnModelCreating`. |
| Query filter test fails after tenant change | EF InMemory caches captured params — use one DbContext per tenant view with a shared `UseInMemoryDatabase(name)`. |
| `tsc --noEmit` TS2339 on `as const` array | Replace `as const` with an explicit `interface` and `readonly Item[]`. |
| `pnpm` "Unsupported engine" warning | Node < 22. Not fatal, but Next 15 builds are flaky on Node 20. |

## Do not

- Do not run `git commit` or `git push` from inside this skill — verification only.
- Do not modify code unless the user explicitly asks for the fix to be applied.
- Do not skip steps to save time. The whole point is that all five must pass.
