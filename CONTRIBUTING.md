# Contributing to Aireq

Thanks for your interest in contributing. This document explains how to propose changes, the workflow we follow, and the standards every contribution must meet. Aireq is currently in early development and most work happens on dedicated branches that map 1:1 to a Story in [`PLAN.md`](PLAN.md).

If you are an AI agent (Claude, ChatGPT, Cursor, etc.), also read [`AGENTS.md`](AGENTS.md) — it is the binding operating manual for non-human contributors.

---

## 1. Before you start

1. Read [`README.md`](README.md) for the project overview.
2. Read [`memory.md`](memory.md) for the durable decision record.
3. Read the relevant Story in [`PLAN.md`](PLAN.md).
4. If touching architecture, read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and the relevant ADRs in [`docs/adr/`](docs/adr/).

## 2. Setting up your environment

```bash
git clone git@github.com:mshanawaz114/aireq.git && cd aireq

# .NET 10 SDK + Node 22 LTS + pnpm 9 + Docker + GitHub CLI required
./scripts/bootstrap.sh    # runs after AIR0002 lands
make dev
```

## 2a. Install pre-commit hooks (one-time)

```bash
./scripts/install-hooks.sh    # idempotent; safe to re-run
```

The hooks run on every `git commit` (under ~3 seconds) and enforce:

- **gitleaks** — refuses to land a secret. The exact same config (`.gitleaks.toml`) runs in CI as a second line of defence.
- **Conventional Commits** — your commit message must start with `feat: …`, `fix(api): …`, `chore: …`, etc. See [`memory.md` §12a](memory.md#12a-branch--commit-conventions).
- **dotnet format** on changed `.cs` files.
- **prettier** on changed `apps/web/**` files.
- Whitespace / EOL / large-file hygiene.

If a hook auto-fixes a file, your commit is **rejected**; re-stage and commit again. This is intentional — it forces you to look at what changed.

To run the full hook suite against the entire tree (e.g. before a big PR):

```bash
pre-commit run --all-files
```

To bypass hooks in a true emergency (then immediately open an incident): `git commit --no-verify`.

## 3. Pick a Story or open one

Every change must trace to a Story ID from [`PLAN.md`](PLAN.md). If your idea isn't covered:

1. Open a GitHub issue using the **Feature request** template.
2. Discuss with the maintainer.
3. Once approved, the maintainer assigns a Story ID and adds it to `PLAN.md`.

Then claim the Story by commenting on the issue or assigning it in Jira.

## 4. Branch naming

| Pattern | Use for | Example |
|---|---|---|
| `AIR####-<slug>` | Foundation / governance | `AIR0002-ci-cd-pipelines` |
| `AIRMVP{N}-<story-id>-<slug>` | MVP work | `AIRMVP1-103-tenant-middleware` |
| `AIRGA1-<story-id>-<slug>` | GA / hardening | `AIRGA1-140-axe-cert` |
| `AIRSCALE-<story-id>-<slug>` | Scale phase | `AIRSCALE-201-browser-extension` |
| `hotfix/<short>` | Urgent production fix | `hotfix/jwt-clock-skew` |
| `release/v<X.Y.Z>` | Release prep | `release/v0.1.0` |

Rules: kebab-case after the ID, no spaces, no uppercase except the prefix. One Story per branch.

## 5. Commit messages — Conventional Commits

```
<type>(<scope>): <subject>

<body — what + why, not how. Wrap at 72 cols.>

Refs: <story-id>
```

**Types:** `feat | fix | chore | docs | test | refactor | perf | build | ci | style | security`.
**Scopes:** `api | worker | web | infra | docs | skills | plugins`.

Every commit references its Story ID in the footer. CI checks this.

### Examples

```
feat(api): add tenant resolution middleware

Resolves tenant_id from JWT claims and stamps the ambient
IHttpContextAccessor for EF Core global query filters.
Integration test enforces cross-tenant isolation.

Refs: AIRMVP1-103
```

```
fix(worker): retry Greenhouse submit on 429 with exponential backoff

Refs: AIRMVP1-303
```

```
docs: clarify accessibility commitment

Refs: AIR0001
```

## 6. Pull request workflow — push and PR are **one command**

We never push without immediately opening a PR. The repo ships [`scripts/push-pr.sh`](scripts/push-pr.sh) for this purpose.

```bash
# 1. Branch off main
git checkout main && git pull origin main
git checkout -b AIRMVP1-103-tenant-middleware

# 2. Make changes, commit in small atomic steps
git commit -m "feat(api): ..."

# 3. One-command push + PR
./scripts/push-pr.sh
# or with an explicit title:
./scripts/push-pr.sh "AIRMVP1-103 tenant middleware"
```

What the script does:
- Refuses to run from `main` / `master`.
- Validates the branch name against the project conventions.
- Pushes the branch (sets upstream on first push).
- Creates a PR against `main` using the commit message as the title/body, plus the project PR template.
- Opens the PR in your browser.

If you must run it manually, **always chain the commands** — never push without opening the PR:

```bash
git push -u origin <branch> && \
gh pr create --base main --fill --title "<story-id> <short>" --web
```

PR title format: `<story-id> <short description>`.

## 7. Definition of Done (universal)

Every PR must meet **all** of these before merge:

- [ ] CI green: build, lint, format, gitleaks, CodeQL, axe-core, all tests.
- [ ] Unit tests cover happy path + at least one failure mode.
- [ ] For UI changes: keyboard-navigable, axe-core clean, contrast ≥ 4.5:1.
- [ ] For data changes: tenant isolation integration test green.
- [ ] For AI calls: routed through `LlmGateway`, cost cap respected, prompt + response logged.
- [ ] No secrets committed (gitleaks clean).
- [ ] `memory.md` updated if a decision was made.
- [ ] ADR added under `docs/adr/` if architectural.
- [ ] One CODEOWNER approval.
- [ ] Branch name + commit footers reference the Story ID.

## 8. Code style

- **C# / .NET:** `dotnet format` enforced in CI. Nullable reference types ON. `var` only when type is obvious. Sealed classes by default.
- **TypeScript / React:** ESLint + Prettier enforced in CI. Strict mode ON. No `any`. Functional components with hooks. No class components.
- **CSS:** Tailwind utility-first. Custom CSS only inside `globals.css` or component-local module CSS, with a comment explaining why.
- **SQL / EF:** prefer EF migrations; raw SQL only when justified in PR description.
- **Tests:** Arrange-Act-Assert. One assertion topic per test. Synthetic data only.

## 9. Accessibility

Read [`ACCESSIBILITY.md`](ACCESSIBILITY.md). Summary: WCAG 2.2 Level AA is the floor, not the ceiling.

## 10. Security & secrets

Read [`SECURITY.md`](SECURITY.md). Summary:

- Never commit a secret.
- `gitleaks` runs in pre-commit and CI; if it flags you, do **not** force-push to bypass — rotate the secret and rewrite history with `git-filter-repo`.
- Report vulnerabilities via the private channel in [`SECURITY.md`](SECURITY.md), not via public issues.

## 11. Reviewing PRs

If you're a reviewer (CODEOWNER):

- Read the linked Story for context.
- Verify the Definition of Done items.
- Prefer suggestions over rewrites — leave authors room to learn.
- Approve only when you would be comfortable owning the change if it broke at 2am.

## 12. Releases

Releases are cut from `release/v<X.Y.Z>` branches by the maintainer.
- Phase 0 → `v0.0.x`
- MVP shipped → `v0.1.0`
- First paying customer → `v1.0.0`

Releases use [Semantic Versioning](https://semver.org/) once we hit `v1.0.0`. Until then, breaking changes can ship on minor bumps with a `BREAKING CHANGE:` footer.

## 13. Community standards

By participating you agree to abide by the [Code of Conduct](CODE_OF_CONDUCT.md).

## 14. Questions?

- General: open a GitHub Discussion.
- Bugs: open an issue with the Bug template.
- Security: see [`SECURITY.md`](SECURITY.md).
- Accessibility accommodation: see [`ACCESSIBILITY.md`](ACCESSIBILITY.md).
