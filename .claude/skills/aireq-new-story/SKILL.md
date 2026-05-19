---
name: aireq-new-story
description: Start a new Aireq story branch from main with the correct naming convention, base, and initial commit setup. Use when the user wants to start work on a specific story id from PLAN.md, or says "start AIRMVP1-104", "new story", "next story", "begin AIR0005", or similar.
---

# aireq-new-story

Cuts a clean branch from `main` for a story, primed for the per-story
workflow.

## Inputs you must collect from the user (or infer)

1. **Story id** — exactly one of:
   - `AIR####` (foundation, four digits, e.g. `AIR0005`)
   - `AIRMVP{N}-XYZ` (MVP, e.g. `AIRMVP1-104`)
   - `AIRGA{N}-XYZ` (GA, e.g. `AIRGA1-110`)
   - `AIRSCALE-XYZ` (scale, e.g. `AIRSCALE-201`)
2. **Slug** — 2-5 kebab-case words describing the work (e.g. `resume-upload-blob-hangfire`).
   If the story id is in `PLAN.md`, lift the slug from the matching row.

## What to do

1. **Confirm `main` is clean** before cutting:
   ```bash
   git status --short
   ```
   If anything is staged or modified, stop and surface to the user — never
   silently stash.

2. **Update local main**:
   ```bash
   git checkout main
   git pull origin main
   ```

3. **Cut the branch**:
   ```bash
   git checkout -b <story-id>-<slug>
   ```

4. **Tell the user** what story you started and link to its row in `PLAN.md`.
   Suggest the next concrete action (e.g. "creating `apps/api/Aireq.Api/...`").

## Commit message template the user should follow

```
<type>(<scope>): <subject>

<body — what + why, not how>

Refs: <story-id>
```

Where:
- `<type>` ∈ {feat, fix, chore, docs, test, refactor, perf, build, ci, style, security}
- `<scope>` ∈ {api, worker, web, infra, docs}
- subject: imperative, ≤72 chars, no trailing period

## Push + PR (when work is done)

```bash
./scripts/push-pr.sh
# or with explicit title:
./scripts/push-pr.sh "<story-id> <short>"
```

**Never** run a bare `git push` — the project rule is push+PR in one chain.

## Do not

- Do not start a new branch if the current branch has uncommitted work — that's how 103's history got fragmented.
- Do not branch off anywhere other than `main` (no stacked branches without telling the user).
- Do not skip the `git pull origin main` step — branching off a stale local main pollutes the diff.
