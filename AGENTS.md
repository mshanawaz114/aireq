# AGENTS.md — Operating manual for AI agents in this repo

> **Audience:** any AI coding agent (Claude, ChatGPT, Cursor, GitHub Copilot, Cody, future SDKs) operating on this codebase.
>
> **Why this file exists:** so the owner doesn't have to re-explain the project to every new tool or session.
>
> **You must follow every rule in this file.** Violations cause merge-blockers and waste the owner's time.

---

## 1. Before you do anything

1. **Read [`memory.md`](memory.md) from top to bottom.** It is the durable record of every decision, requirement, and constraint. Treat it as non-negotiable unless the owner explicitly overrides.
2. Read [`PLAN.md`](PLAN.md) §Phases and the section for the current Story you are working on.
3. Read [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) if you are touching backend modules, data model, or AI calls.
4. If anything in the task contradicts memory.md, **stop and ask** rather than guess.

## 2. Identity & non-negotiables

| Decision | Locked value | Source |
|---|---|---|
| Brand | **Aireq** ("AI-rek") | memory.md §0 |
| Backend stack | .NET 10 LTS · ASP.NET Core 10 | memory.md §5 |
| Frontend | Next.js 15 (App Router) | memory.md §5 |
| Database | Neon Postgres + pgvector | memory.md §5 |
| LLM | Claude Haiku (cheap) + Sonnet (rewrites); always through `LlmGateway` | memory.md §9 |
| Multi-tenant | Yes, from day 1; isolation enforced by integration test | memory.md §5, §6 |
| Browser automation | Tier A→D approach; never bypass robots.txt or ToS | memory.md §8 |
| Accessibility target | WCAG 2.2 Level AA | memory.md §12c |
| Security floor | No secrets in git, ever | memory.md §12d |

Do not change any of these on your own initiative. If a different choice would be objectively better, **propose it as an ADR** in `docs/adr/` and await owner approval before implementing.

## 3. Branch and commit rules

### Branch names

| Pattern | Use for |
|---|---|
| `AIR####-<kebab-slug>` | Foundation / governance / ops (4-digit number) |
| `AIRMVP{N}-<story-id>-<kebab-slug>` | MVP iteration N work |
| `AIRGA1-<story-id>-<kebab-slug>` | GA / hardening |
| `hotfix/<short>` | Production-only urgent fixes |
| `release/v<X.Y.Z>` | Release branches |

One Story per branch. One branch per PR.

### Push & PR are **one command**, never two

When you finish a branch, you do not run `git push` and then `gh pr create` as separate steps. You always use `scripts/push-pr.sh`, which:

1. Validates the branch name against the conventions above.
2. Refuses to run from `main` / `master`.
3. Pushes the branch (sets upstream on first push).
4. If a PR doesn't exist yet, creates one against `main` with title auto-filled from the commit subject.
5. Opens the PR in the browser.

```bash
./scripts/push-pr.sh                                 # title from commit
./scripts/push-pr.sh "AIRMVP1-103 tenant middleware" # explicit title
```

When you write instructions for the owner, **always give the single chained command**, never split push and PR. Example:

```bash
cd ~/Documents/aireq && \
git push -u origin AIRMVP1-103-tenant-middleware && \
gh pr create --base main --fill --title "AIRMVP1-103 tenant middleware" --web
```

…or, equivalently and preferred once `scripts/push-pr.sh` is on the branch:

```bash
cd ~/Documents/aireq && ./scripts/push-pr.sh
```

### Commit messages — [Conventional Commits](https://www.conventionalcommits.org)

```
<type>(<scope>): <subject under 72 chars>

<body — what changed and why, not how. Wrap at 72 cols.>

Refs: <story-id>
```

Types: `feat | fix | chore | docs | test | refactor | perf | build | ci | style | security`.
Scopes: `api | worker | web | infra | docs | skills | plugins`.

**Every commit references a Story ID** in the footer. No exceptions.

## 4. Hard rules (CI will block you if you violate)

1. **No secrets in git.** Not in code, not in tests, not in fixtures, not in comments. Use `.env.local.example` placeholders.
2. **No direct LLM SDK calls.** All AI calls go through the `LlmGateway` abstraction so cost caps and audit logging apply uniformly.
3. **No raw SQL without a tenant filter** unless the query is admin-scoped and clearly annotated.
4. **No new front-end component without keyboard navigation and axe-core passing** locally.
5. **No new outbound email path without explicit owner-approval mode** as the default (`require_approval=true`).
6. **No new third-party dependency** without a one-line justification in the PR description.
7. **No new `console.log` / `Console.WriteLine` in committed code.** Use structured logging (Serilog / pino).

## 5. Style preferences

- **Prose:** clear, concise, professional. No corporate fluff. No emoji unless owner uses them first.
- **Code comments:** sparse. Comments explain *why*, not *what*. Prefer self-explanatory names.
- **Tests:** Arrange-Act-Assert. Each test owns its setup. No shared mutable fixtures across files.
- **Error handling:** prefer `Result<T,Err>` / discriminated unions over exceptions for expected failures.
- **Naming:** `PascalCase` for C# types, `camelCase` for C# locals/params, `kebab-case` for files (where the language doesn't dictate), `SCREAMING_SNAKE_CASE` for env vars.

## 6. Working with `memory.md`

`memory.md` is **append-mostly**. Specifically:

- **Append** new entries to §16 (Conversation Log) — dated, brief, factual.
- **Update in place** §0 (Quick Project Card), §5 (Architecture), §6 (Data Model), §11 (Security & Compliance) only when a decision genuinely changes. Never delete history; mark old entries as superseded.
- **Open Questions (§12)** should always have items checked or moved out as they get resolved.

If you make any architectural change, write an ADR in `docs/adr/####-name.md` and cross-link it from `memory.md`.

## 7. Privacy & data handling

- Resumes contain PII. Never log raw resume content in production logs. Hash or summarize.
- Recruiter emails are likewise PII. Same rule.
- Test fixtures must use **synthetic data**. Never check in a real consultant's resume — not even your own.

## 8. Cost discipline

- Default LLM model for any task is **Claude Haiku 4.5**. Only escalate to Sonnet when output quality demonstrably needs it.
- Embeddings via `text-embedding-3-small`. Do not use larger embedding models without ADR.
- Cache deterministic LLM calls (e.g. JD parses) by content hash for at least 30 days.
- Per-tenant monthly cap enforced in `LlmGateway`; do not bypass.

## 9. Accessibility-first frontend

For every new page or component:

- Start with semantic HTML; reach for ARIA only when semantic is insufficient.
- Tab order matches visual order.
- All interactive elements have visible focus states.
- All images / icons have `alt` text or `aria-hidden` if decorative.
- Color is never the sole information channel.
- Contrast ≥ 4.5:1 for text, ≥ 3:1 for large text and UI components.
- Forms have labels (real `<label>`, not placeholders).
- Errors are announced to assistive tech (`aria-live="polite"` where appropriate).

axe-core run by CI must show **zero violations**.

## 10. What to do when stuck

1. Re-read the Story's acceptance criteria in `PLAN.md`.
2. Check `docs/adr/` for a relevant decision.
3. Check `memory.md` Conversation Log for prior context.
4. **Ask the owner** in PR review or by leaving a `TODO(@owner): question…` comment with a clear yes/no question. Do not silently make assumptions on architectural choices.

## 11. Pull request checklist (paste this into every PR)

```
## Story
Refs: <AIR-id>

## What
- (1-3 bullets)

## Why
- (1-3 bullets)

## Checks
- [ ] CI green
- [ ] memory.md updated if decision made
- [ ] ADR added if architectural
- [ ] Tests cover happy path + 1 failure mode
- [ ] If UI: keyboard nav verified, axe-core clean, contrast ≥ 4.5:1
- [ ] If data: tenant-isolation test still green
- [ ] If LLM: routed through LlmGateway, cost cap respected
- [ ] No secrets committed (gitleaks clean)
- [ ] Branch name + commit footer match the Story ID

## Screenshots / demo
(for UI changes)
```

## 12. Speed-of-iteration notes

- The owner wants the MVP shipped in 4 weeks. Bias to small PRs and forward progress over polish.
- When you must choose between two reasonable approaches and the difference is < 10% effort, pick the simpler one and add a TODO with the rationale.
- Surface blockers to the owner immediately. Don't burn an afternoon on a stuck dependency upgrade — ask.

---

**Last updated:** 2026-05-17 (this branch).
**Next review:** at end of Phase 1.
