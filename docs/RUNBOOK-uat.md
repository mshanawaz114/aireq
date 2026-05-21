# MVP UAT Runbook — recruiter CRM + close-out (AIRMVP1-407)

The Week-4 / MVP gate: prove the **full loop closes** — a real recruiter reply is
received, classified, escalated to the owner, and the owner can act; a second
user can self-sign-up and reach matches; and the monthly bill stays **≤ $30**.

The automated half is covered by
`tests/Aireq.Api.Tests/E2E/RecruiterReplyE2ETests.cs` (inbound → classify →
escalate → notify → follow-up) and `PipelineE2ETests.cs` (discover → apply).
This runbook is the **manual** half — real Gmail, real LLM, real Stripe test
mode.

## 0. Safety first

> Three independent kill-switches keep UAT from emailing or charging anyone for
> real. Confirm all three before starting:
>
> - `FEATURES__ENABLE_LIVE_SUBMIT=false` — no application is sent to an employer.
> - `FOLLOWUP__SENDLIVE=false` and `DIGEST__SENDLIVE=false` — emails dry-run
>   (audited in `EmailLog`, nothing leaves).
> - `STRIPE_SECRET_KEY=sk_test_…` — **test mode only**. Never a live key in UAT.

Pre-flight `.env.local`:

- All W1–W3 keys from `RUNBOOK-bugbash.md` (DB, Groq, Gemini, Azurite, job sources).
- `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` — Gmail OAuth (test consent screen).
- `STRIPE_SECRET_KEY` (test), `STRIPE_WEBHOOK_SECRET`, `STRIPE__PRICEID` (test price).
- `FOLLOWUP__AUTOSEND=false` (owner-approval default).

## 1. Boot

```bash
azurite --silent --location ~/.azurite &
cd ~/Documents/aireq && make dev                 # api :5180 · worker :5090 · web :3000
stripe listen --forward-to localhost:5180/api/billing/webhook   # test-mode webhooks
```

## 2. Second-user self-signup (gate criterion)

In a fresh browser profile: visit `/` (the landing page) → **Join the waitlist**
(confirm the success state) → **Sign up** → create a new tenant + owner. Land on
the dashboard. *This proves a brand-new user can self-serve.*

## 3. Discover → tailor → apply

Follow `RUNBOOK-bugbash.md` §2–4 to get at least one application out via the
**email tier** (so there's an `EmailLog` with `purpose=apply` + a recruiter
recipient to thread a reply against). Note the match.

## 4. Connect Gmail + receive a reply (gate criterion)

1. **Settings → Integrations → Connect Gmail** (`/api/integrations/gmail/connect`)
   → complete Google consent → bounced back to the app, status shows the
   connected mailbox.
2. From a *separate* email account, reply to the application email **from the
   recruiter address Aireq applied to** (or send a fresh email from that address
   to the connected inbox referencing the role).
3. Within ~5 min the inbound poller threads it. Verify:
   - **Inbox / thread view**: the reply appears as an inbound message.
   - The thread's match advances (`Reply` / `Interview` / `Rejected`).
   - **Escalations**: if the reply needs a human (interview/info/scheduling), an
     open escalation card appears.
   - **Notifications**: a bell notification fired (live if you keep a tab open;
     otherwise on refresh — see the known SignalR-backplane limitation below).

## 5. Act on the escalation

Open the escalation → read the AI summary + sentiment → reply to the recruiter
from Gmail directly → **Resolve** the escalation (`POST /api/escalations/{id}/resolve`).
Confirm it leaves the open queue.

## 6. Follow-up nudge approval

For an application with **no** reply after `FOLLOWUP__FIRSTNUDGEAFTERDAYS`, the
hourly planner drafts a nudge. Verify:

- **Follow-ups** queue shows a `Pending` draft (`GET /api/followups`).
- **Approve** it → next pass sends it (dry-run; `EmailLog purpose=followup`,
  status `dry_run`). Confirm the row flips to `Sent`.
- A reply arriving before send **cancels** the nudge.

## 7. Daily digest

Trigger the digest (or wait for the `DIGEST__CRON`): confirm an `EmailLog`
`purpose=digest` row for the tenant summarising the day, dry-run, addressed to
the owner. Tenants with no activity get nothing.

## 8. Billing (Stripe test mode)

1. **Settings → Billing**: a brand-new tenant shows **Free trial** with a date
   ~14 days out and **Active access**.
2. **Subscribe** → Stripe test checkout (card `4242 4242 4242 4242`) → back to
   the app. The webhook flips status to **Active**; the page shows a renewal date.
3. **Manage billing** → the Stripe customer portal opens.
4. Cancel in the portal → webhook → status reflects `canceled`.
5. (Optional) Fast-forward a tenant's trial: confirm `trial_expired` blocks
   access and the **Subscribe** CTA shows.

## 9. Cost check (gate criterion)

Open **Dashboard → LLM spend** (and the Groq/Gemini/Anthropic consoles + Stripe
test mode shows $0 real). Confirm the cumulative MVP bill is **≤ $30**.

## 10. What to record

For each step: pass/fail, screenshots of the inbox/escalation/billing states,
and any error in the API/worker logs. File anything that 500s, fails to thread a
reply, mis-classifies obviously, double-sends a nudge, or mishandles a webhook
into the backlog (`docs/BACKLOG.md`).

## Epic gate (MVP DONE) — sign-off checklist

- [ ] A real recruiter reply was received, threaded, classified, and escalated.
- [ ] A second user self-signed-up and reached matches.
- [ ] Follow-up approve → send (dry-run) works; reply-race cancels.
- [ ] Stripe test checkout + portal + webhook round-trip works; trial gates access.
- [ ] Cumulative bill ≤ $30.
