# W3 Bug-Bash Runbook — 10 applies end-to-end (AIRMVP1-307)

The goal of the Week-3 gate: drive **10 real applications end-to-end** across the
three automated tiers (API, Playwright, email) and confirm each lands a
`Submission` row with the right channel + status, an audit payload, and a
tailored PDF in blob storage.

The automated half of this is covered by `tests/Aireq.Api.Tests/E2E/PipelineE2ETests.cs`.
This runbook is the **manual** half — exercising real LLMs, real job boards, and
(optionally) real submissions.

## 0. Safety first

> **`FEATURES__ENABLE_LIVE_SUBMIT` controls whether anything is actually sent to
> an employer.** Leave it `false` for the bug-bash unless you have explicitly
> chosen a board you intend to really apply to. In dry-run, every tier records a
> `Submission` row + audit payload and (for Playwright) a screenshot, but sends
> nothing.

Pre-flight `.env.local` checklist:

- `DATABASE_URL_DEV` — Neon, reachable.
- `GROQ_API_KEY` — set (parsing, scoring, tailoring, cover notes).
- `GEMINI_API_KEY` — set (embeddings).
- `AZURE_BLOB_CONNECTION_STRING=UseDevelopmentStorage=true` — Azurite running.
- Job source keys: at least the keyless ATS sources work; add `ADZUNA_*` /
  `USAJOBS_*` for breadth.
- `FEATURES__ENABLE_LIVE_SUBMIT=false` (keep it off).
- Playwright browsers installed once: `~/.dotnet/tools/playwright install chromium`.

## 1. Boot

```bash
azurite --silent --location ~/.azurite &     # blob emulator
cd ~/Documents/aireq && make dev             # api :5180 · worker :5090 · web :3000
```

## 2. Seed a consultant + resume

In the web app (http://localhost:3000): sign up → **Consultant Profile** → fill
details + **upload a real resume** (PDF). Wait for the worker to parse + embed it
(watch worker logs or the Hangfire dashboard at http://localhost:5090/hangfire).

## 3. Run the discovery pipeline

```bash
curl -X POST localhost:5090/jobs/pipeline   # ingest -> embed -> match -> score (chained)
```

Confirm on **Dashboard** (Pipeline metrics tile): active jobs > 0, embedded > 0,
matches > 0, avg score sensible. Open **Job Matches** — scored cards with
reasoning + missing-keyword chips. Expand "ATS keyword analysis" on a few.

## 4. Tailor + submit 10 matches

For each of 10 matches spanning different sources (greenhouse / lever / a
non-API source to exercise Playwright + email tiers):

1. Click **Tailor & apply** → wait → reload (status = Tailored).
2. Click **Submit application**.
3. Open **Submissions** — verify a row with the expected **channel** and
   `dry_run` status, and expand the audit payload.

Expected channel selection:

| Job source | Tier exercised | Channel |
|---|---|---|
| greenhouse | A (API) | `Api` |
| lever | A (API, experimental) | `Api` |
| a source w/ no API + a hosted form | B (Playwright) | `Portal` (+ screenshot in payload) |
| a source whose JD contains an email | C (email) | `Email` |
| anything else | D (manual) | `Manual` (`pending_manual`) |

## 5. What to record

For each of the 10: source, channel chosen, status, ATS coverage before/after
(from the TailoredResume diff), and any error in the worker logs. File a bug for
anything that 500s, mis-routes a tier, or produces an empty/garbled tailored PDF.

## 6. Going live (later, deliberately)

Only after the dry-run bash is clean: pick ONE real Greenhouse board you intend
to apply to, set `FEATURES__ENABLE_LIVE_SUBMIT=true`, restart the worker, and
submit that single match. Verify the application landed on the employer side.
Revert the flag afterward.
