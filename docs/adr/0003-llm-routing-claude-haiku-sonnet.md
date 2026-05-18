# ADR 0003 — Route LLM calls through `LlmGateway`; default to Claude Haiku, escalate to Sonnet

- **Status**: accepted
- **Date**: 2026-05-17
- **Deciders**: @mshanawaz114
- **Story / Driver**: AIRMVP1-105, AIRX-E11

## Context

The product makes frequent LLM calls across the pipeline — resume parsing, JD parsing, skill extraction, classification of recruiter replies, resume rewriting, cold-email drafting. Cost discipline matters (free-tier infra; < $30/month at MVP load) and quality matters more for rewriting than for classification. The MVP team is a single owner; we can't afford a Bedrock + Vertex + OpenAI provider matrix on day one.

## Decision

Every LLM call in Aireq goes through `LlmGateway`. The gateway:

- accepts a typed request (model bucket, max_tokens, prompt, structured-output schema if any),
- routes to the cheapest provider/model bucket that meets the quality bar,
- enforces a per-tenant monthly token budget (Haiku and Sonnet tracked separately),
- logs prompt + response + cost + model into the `Messages` / audit table whenever the call is outbound on behalf of a tenant.

Initial routing:

- **Haiku 4.5**: resume parsing, JD parsing, skill extraction, inbound classification.
- **Sonnet 4.6**: resume rewriting per JD, cold-email drafting.

Token-budget guardrail (per consultant / month): 250k input + 50k output Haiku; 80k input + 20k output Sonnet. Hard cap at gateway level.

Swap path: if Azure OpenAI credits become available, the gateway gains a second `IChatCompletionsProvider` and we A/B without touching call sites.

## Consequences

- **Positive**
  - One choke point for cost, audit logging, retries, and prompt-injection scrubbing.
  - Call-site code stays clean — `await llm.AnalyzeResumeAsync(text, ct)` not `await anthropic.MessagesAsync(...)`.
  - Provider swaps are local changes; no caller rewrites.
- **Negative**
  - Adds a small amount of indirection. A wrong route (Sonnet for parsing) shows up as cost spike, not a runtime error — we mitigate with weekly cost-by-bucket dashboards.
- **Neutral**
  - Gateway versioned alongside the API; minor surface, low maintenance load.

## Alternatives considered

1. **Direct SDK calls everywhere** — simple but disperses cost control and audit logging. Rejected.
2. **LangChain / Semantic Kernel as gateway** — heavier dependency, less control over prompt + cost telemetry. Deferred.
3. **Self-hosted open-weights model** — too expensive at MVP scale and quality below Haiku for our prompts. Deferred to Phase 3.

## Links

- Source: `apps/api/Aireq.Api/Llm/LlmGateway.cs` (lands in AIRMVP1-105)
- Memory: `memory.md` §9
- Cost telemetry: rolls into AIRGA1-E3 observability
