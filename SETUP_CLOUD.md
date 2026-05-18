# SETUP_CLOUD.md — manual cloud account playbook

> One-time setup. ~30 minutes if you have nothing yet; ~10 if you have an Anthropic key already.
> After this, `make dev` boots the full stack locally.

This file documents the **manual** steps a human has to do at provider portals. Everything else is automated. When you finish, paste the captured values into `.env.local` and run `make dev`.

Track your progress with the checkboxes.

---

## 1. Neon Postgres — dev branch

Neon is our serverless Postgres. Free tier is 0.5 GB and includes pgvector.

- [ ] Sign in at <https://console.neon.tech> with your GitHub account.
- [ ] Create a new project named **`aireq-prod`**.
  - Region: closest to you. (US East works for most.)
  - Postgres version: 16 or later.
- [ ] Inside the project, open the **Branches** tab and create a branch named **`dev`** off `main`.
  - This becomes your isolated dev database. Costs nothing because Neon only charges for the diff.
- [ ] On the `dev` branch, **Settings → Extensions** → enable **`vector`**. This is pgvector — we need it for resume/job embeddings in AIRMVP1-204.
- [ ] On the `dev` branch, **Connection details** → **Pooled connection** → copy the connection string. It looks like:
  ```
  postgresql://USER:PASS@ep-xxxx-pooler.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require
  ```
- [ ] Paste it into your local `.env.local` as `DATABASE_URL_DEV=...`.

> **Why pooled?** Neon's serverless compute can sleep. The pooler keeps connections healthy across cold starts.

Repeat for a **prod** branch later — for now you only need dev.

---

## 2. Anthropic Claude — API key

The LLM provider for parsing, classifying, and rewriting resumes.

- [ ] Sign in at <https://console.anthropic.com>.
- [ ] **Settings → Billing** → add a payment method.
- [ ] **Settings → Plans & billing** → set a **monthly spend cap of $30**. (Important: this is your guardrail. Aireq's `LlmGateway` also enforces a per-tenant cap, but the provider-side hard cap is your safety net.)
- [ ] **API Keys** → **Create Key** named `aireq-dev`.
- [ ] Copy the key (starts with `sk-ant-`). You only see it once.
- [ ] Paste it into your local `.env.local` as `ANTHROPIC_API_KEY=sk-ant-...`.

Pricing reminder (May 2026 list prices):
- Haiku 4.5: roughly $1 / million input tokens, $5 / million output.
- Sonnet 4.6: roughly $3 / million input tokens, $15 / million output.

At MVP load (~250k Haiku input / 50k output + 80k Sonnet input / 20k output per consultant per month), one consultant costs about **$1-2/month** of LLM spend.

---

## 3. Resend — email sending domain

For cold-outreach emails (AIRMVP1-305) and inbound thread tracking. Free tier is 3,000 emails/month.

- [ ] Sign in at <https://resend.com>.
- [ ] **API Keys** → create key `aireq-dev` with scope `Sending access`.
- [ ] Paste it into `.env.local` as `RESEND_API_KEY=re_...`.

Domain DKIM (do this **after** you've bought `aireq.com`):

- [ ] **Domains → Add Domain** → enter `aireq.com`.
- [ ] Resend gives you 3 DNS records. Add them to your registrar's DNS (Cloudflare is easiest if you bought the domain at Cloudflare Registrar):
  - 1 `MX` record (priority 10 → `feedback-smtp.<region>.amazonses.com`)
  - 1 `TXT` SPF record (`v=spf1 include:amazonses.com ~all`)
  - 1 `CNAME` DKIM record (long random subdomain → AWS SES target)
- [ ] Also add a DMARC record yourself:
  ```
  Name: _dmarc.aireq.com
  Type: TXT
  Value: v=DMARC1; p=quarantine; rua=mailto:dmarc@aireq.com
  ```
- [ ] Wait 5-15 minutes; click **Verify** on Resend.

You can skip this section entirely for AIRMVP1-101 — email doesn't fire until AIRMVP1-305.

---

## 4. Azure — verify resource group

You already have Azure free tier active. We won't deploy to Azure until AIRMVP1-107, but let's make sure the resource group is set up.

- [ ] Sign in at <https://portal.azure.com>.
- [ ] **Resource groups → Create**:
  - Name: `aireq-rg`
  - Region: same as Neon (e.g., East US 2 or West Europe — pick low latency to Neon).
- [ ] (Optional, can wait): Inside the resource group, create:
  - Storage account `aireqblobsdev` (Standard LRS, cool tier — stores resumes).
  - Container Apps environment `aireq-cae` (Consumption plan).
  - Key Vault `aireq-kv-dev`.

We'll script all of this with Bicep in AIRMVP1-107. For now, the resource group existing is enough.

---

## 5. Domains — register `aireq.com`, `aireq.ai`, `aireq.io`

If not done yet:

- [ ] Register at **Cloudflare Registrar** (at-cost pricing, free WHOIS privacy):
  - `aireq.com` (primary — web + app + email)
  - `aireq.ai` (alternate web)
  - `aireq.io` (defensive)
- [ ] In Cloudflare DNS, leave existing records empty for now. We'll add:
  - `A` / `CNAME` to Vercel for the marketing site (AIRMVP1-405).
  - `CNAME api.aireq.com` to Azure Container Apps (AIRMVP1-107).
  - DKIM/DMARC records (Resend, see §3).

Total cost: ≈ $12 (.com) + $50 (.ai) + $10 (.io) ≈ **$72 / year**.

---

## 6. Verify locally

Once the values in §1 and §2 are in `.env.local`:

```bash
make doctor    # prints versions of required tools
make bootstrap # installs everything, idempotent
make dev       # boots api + worker + web
```

Open <http://localhost:3000>. You should see:
- The dashboard page with a sidebar.
- A green **API ok · v0.0.0 · postgres=ok** badge in the top-right (polling `/health/ready` every 15 seconds).

If the badge is green, **AIRMVP1-101 is done**.

If red:
- `API down` → the .NET API isn't reachable on `localhost:5080`. Check `make api` in a separate terminal.
- `postgres=down` → connection string in `.env.local` is wrong, or pgvector isn't enabled on the dev branch.

---

## 7. Per-feature cloud setup (later stories)

Things that are **not** required for AIRMVP1-101 but listed here for reference so you don't have to re-discover them:

| Provider | Story it's needed for | Action |
|---|---|---|
| **Adzuna** | AIRMVP1-201 | Sign up at <https://developer.adzuna.com>, get free tier (1k calls/mo). |
| **USAJobs** | AIRMVP1-201 | Register at <https://developer.usajobs.gov>; needs your email as User-Agent. |
| **Hunter.io** | AIRMVP1-305 | Free tier: 25 lookups/month, used to find recruiter emails. |
| **Plausible** | AIRMVP1-405 | $9/mo for analytics on the public landing page. |
| **Sentry** | AIRMVP1-403 | Free tier, errors only initially. |
| **Stripe** | AIRMVP1-406 | Test mode only until first paying customer. |
| **Gmail API** | AIRMVP1-401 | OAuth app at <https://console.cloud.google.com>; per-tenant consent. |

When you hit the relevant story, this table tells you which portals you need to visit.
