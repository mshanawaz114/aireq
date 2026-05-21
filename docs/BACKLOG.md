# Backlog — groomed at MVP close (AIRMVP1-407)

Captured during the Week-4 bug-bash. Nothing here blocks the MVP epic gate; these
are the known limitations and deferred items, triaged for Phase 2
(`AIRGA1-*`) and beyond. Source of truth for *decisions* remains `memory.md`;
this file is the running to-do.

## Known limitations (shipped intentionally for MVP)

- **SignalR has no backplane.** The notifications hub pushes live only for events
  raised *in the API process*. Worker-raised events (replies, escalations,
  follow-ups) are durable (DB rows) and surface on the client's next fetch /
  reconnect, not instantly. → Phase 2: Redis backplane (or poll-on-interval in
  the web client as a stopgap).
- **Gmail polling, not push.** Inbound replies are pulled every ~5 min via
  `messages.list` with a time cursor. Fine for MVP latency; Gmail `watch` +
  Pub/Sub push is a GA upgrade. Dedupe is by message id, so re-polling is safe.
- **Secrets at rest in plain columns.** `GmailAccount` refresh/access tokens live
  unencrypted for MVP (noted in the entity). → GA security pass: Key Vault /
  column encryption + PII purge.
- **Tokens / single mailbox per tenant.** One connected Gmail per tenant; no
  multi-inbox or shared-team routing yet.
- **Email sends gated off by default.** `FOLLOWUP__SENDLIVE` / `DIGEST__SENDLIVE`
  / `FEATURES__ENABLE_LIVE_SUBMIT` all default false. Going live is a deliberate,
  per-channel flip after domain warmup.
- **LLM on the free tier (Groq + Gemini).** Quality/throughput ceilings apply;
  swap to Anthropic via `LLM__PROVIDER=anthropic` once revenue justifies it
  (the gateway already supports it).

## Deferred web UI (backend shipped, screens pending)

The Week-4 backend APIs are live and tested; their dedicated screens are thin or
stubbed and should be built out in Phase 2:

- Inbox / thread conversation view (reads `RecruiterThread` + `Message`).
- Escalations queue page (reads `/api/escalations`, resolve action).
- Follow-up approval queue page (reads `/api/followups`, approve/cancel).
- Notification bell + live feed (reads `/api/notifications`, SignalR `/hubs/notifications`).

## Deferred foundation tickets

- **AIR0002 — CI/CD pipelines.** Build/test/lint/web-build + migration gating in
  GitHub Actions. Also: clear the `NU1903` test-only transitive warning
  (`System.Security.Cryptography.Xml`) via a documented, scoped `NoWarn` here.
- **AIR0005 — accessibility tooling in CI.** axe / Lighthouse budget on the web
  build; the components already follow a11y patterns (skip link, labels, roles).

## Phase 2 candidates (AIRGA1-*)

- Multi-user roles + permissions beyond owner/admin/viewer.
- Auto-reply *drafting* for recruiter replies (compose, owner-approve, send) —
  reuses the classifier + the follow-up approval pattern.
- Refresh-token rotation for app auth (`AIRMVP1-103b`, noted in `JwtTokenService`).
- Generated typed API client from OpenAPI (replace the hand-written `lib/api.ts`).
- Observability: structured request tracing + per-tenant cost dashboards.
- Deliverability: webhook reconcile of Resend events back onto `EmailLog`.
