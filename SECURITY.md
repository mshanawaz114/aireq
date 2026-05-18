# Security policy

Aireq stores resumes, recruiter inboxes, OAuth tokens for Gmail, and Stripe customer data. We take security incidents seriously and welcome responsible disclosure.

## Reporting a vulnerability

Please **do not** open a public GitHub issue for security findings. Instead:

- Email **security@aireq.com** with a description of the issue, reproduction steps, and (if possible) a proof of concept.
- Or use GitHub's private vulnerability reporting: <https://github.com/mshanawaz114/aireq/security/advisories/new>.

We aim to acknowledge new reports within **2 business days** and to provide a substantive response within **5 business days**. We commit to a **90-day responsible-disclosure window** before public disclosure unless the report is materially time-critical.

## Scope

**In scope**
- The hosted Aireq application (production URL once public).
- The `mshanawaz114/aireq` repository.
- The official API at `https://api.aireq.com/*` (post-launch).

**Out of scope (please do not test these)**
- Denial-of-service against production.
- Social engineering of Aireq staff or contributors.
- Physical or network-layer attacks on cloud providers (Azure, Vercel, Neon).
- Vulnerabilities requiring a previously compromised end-user machine.
- Marketing / docs sites that don't store user data.

## Severity & rewards

Aireq is pre-revenue and does not yet operate a paid bug-bounty program. We will:

- Publicly credit reporters who request it (with permission).
- Coordinate CVE assignment for confirmed vulnerabilities.
- Backfill a formal bounty program once we have paying customers.

## Threat model snapshot

The current top-of-mind risks, in priority order:

| Rank | Class | Mitigations in place |
|------|-------|----------------------|
| P0 | Cross-tenant data leak via query-filter bypass | EF Core global query filters; tenant-isolation integration test (`tests/Aireq.Api.Tests/Tenancy/QueryFilterTests.cs`); `IgnoreQueryFilters()` only on whitelisted endpoints. |
| P1 | LLM prompt injection from resume content into outbound email | LlmGateway logs every prompt + response; outbound email requires explicit owner approval in v1; resume content stripped of executable-looking strings before model calls. |
| P1 | Playwright credential leak | Per-tenant encrypted storage of ATS credentials; never echoed in logs; rotated on suspicion. |
| P2 | Token theft via XSS on dashboard | Access tokens in localStorage today (MVP); planned move to httpOnly cookie + strict CSP in `AIRMVP1-405`. |

## Coordinated channels

- **Anthropic / OpenAI prompt-related issues** — we forward to the provider's report channel and notify the original reporter when we hear back.
- **Dependency vulnerabilities (Dependabot, CodeQL alerts)** — auto-triaged daily; user-impacting issues become tickets within 24 hours.

## Hall of Fame

Contributors who reported a confirmed vulnerability:

_None yet — be the first._
