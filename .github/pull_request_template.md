<!--
Aireq PR template — keep it terse but complete.

If your work doesn't map to a Story ID in PLAN.md, stop and open the Story
first (see CONTRIBUTING.md §3).
-->

## Story

Refs: **AIRxxxx-...** <!-- Story ID; this MUST be present -->

## What

<!-- 1–3 sentences. What does this PR add/change/remove? -->

## Why

<!-- 1–3 sentences. Why now, and what was the alternative? -->

## How (only if non-obvious)

<!-- A short list, an ADR link, or a diagram. Skip for refactors / cosmetics. -->

## Definition of Done checklist

- [ ] Branch follows naming convention (`AIR####-…` or `AIRMVP{N}-…-…`).
- [ ] Every commit references the Story ID in the footer (`Refs: …`).
- [ ] CI is green (build, test, lint, gitleaks, CodeQL, axe-core when applicable).
- [ ] Unit tests cover the happy path and at least one failure mode.
- [ ] For user-facing changes: keyboard-navigable, axe-core clean, contrast ≥ 4.5:1.
- [ ] For data-handling changes: the tenant-isolation integration test is still green.
- [ ] For LLM calls: routed through `LlmGateway`, cost cap respected, prompt + response logged.
- [ ] `memory.md` updated if a decision was made or scope changed.

## Screenshots / clips (optional)

<!-- Drag images here for UI work, or paste before/after for refactors. -->

## Risk

<!-- One of: trivial · medium · high (justify high). -->
