# Research: CoffeeShop Ordering System

**Date**: 2026-02-23
**Sources**: `constitution.md` (v1.6.2) + `spec.md` (24 FRs) + `research_Feb2026.md`

> Full confidence analysis in [`/research_Feb2026.md`](/research_Feb2026.md).
> This file contains the distilled decisions relevant to implementation planning.

---

## Decisions

### D-001: Communication — counter ↔ barista/kitchen

- **Decision**: `System.Threading.Channels.Channel<T>` (in-process, async)
- **Rationale**: All three run in the same .NET host. In-process channels give zero-overhead dispatch, type safety, and no external broker (Constitution VII). Battle-tested .NET primitive.
- **Alternatives considered**: MassTransit, RabbitMQ — rejected (external broker, violates Constitution VII).

### D-002: Communication — counter ↔ product-catalog

- **Decision**: MCP protocol over HTTP/SSE (out-of-process)
- **Rationale**: Product-catalog is stateless reference data. Running it out-of-process as an MCP Server demonstrates the MCP pattern at the appropriate architectural boundary without over-engineering.
- **Alternatives considered**: In-process singleton — rejected (loses MCP demonstration value; product-catalog is meaningfully independent).

### D-003: MAF + Copilot SDK Integration

- **Decision**: `copilotClient.AsAIAgent(sessionConfig, ownsClient: false)` pattern
- **Rationale**: Canonical pattern from `Agent_With_GitHubCopilot` sample. `CopilotClient` is a DI singleton, shared across agents with `ownsClient: false`. No ProviderConfig or `GITHUB_TOKEN` env var required for the MAF path — Copilot CLI handles auth.
- **Alternatives considered**: Direct `CreateSessionAsync` (chat turn path) — both paths coexist; MAF path is primary for tool-calling agents. Direct Copilot SDK without MAF — rejected (bypasses agent loop abstraction).

### D-004: Streaming — RunStreamingAsync + AG-UI

- **Decision**: `RunStreamingAsync` mandatory for order fulfilment. `RunAsync` banned.
- **Rationale**: FR-020 requires streaming status at 4 boundaries. AG-UI protocol surfaces MAF stream updates to frontend via CopilotKit.
- **Boundaries**: (a) catalog lookup started, (b) items dispatched, (c) per-worker completion, (d) final result.
- **Alternatives considered**: Polling — rejected (poor UX, unnecessary complexity).

### D-005: Frontend Interaction Model

- **Decision**: CopilotKit hybrid — chat sidebar (`@copilotkit/react-ui`) + structured page (menu grid, order summary panel) updated via `useCopilotReadable` / `useCopilotAction`.
- **Rationale**: FR-017. Both surfaces are mandatory. Agent drives state changes; page reflects them reactively. Generative UI components co-located in `src/components/copilot/`.
- **Alternatives considered**: Pure chat — rejected (FR-017 mandates structured page). Pure structured page — rejected (FR-017 mandates chat sidebar).

### D-006: Parallel Dispatch (Mixed Orders)

- **Decision**: `Task.WhenAll` on both barista and kitchen completion channels. If one fails, still await the other.
- **Rationale**: Constitution mandates parallel dispatch for mixed orders. Sequential would mislead on pickup time.
- **Alternatives considered**: Sequential `await` — rejected (degrades performance, violates constitution).

### D-007: MCP Unreachability

- **Decision**: Fail-fast — return `"Menu is currently unavailable — please try again shortly"`. Do not place the order. No cache, no fallback catalog.
- **Rationale**: FR-021. Serving stale catalog data risks orders for unavailable or incorrectly-priced items.
- **Alternatives considered**: In-memory cache — rejected (FR-021 explicitly bans fallback).

### D-008: Order Cancellation Boundary

- **Decision**: Cancel only before `preparing`. Same boundary as modification.
- **Rationale**: FR-022 / FR-010/011. Once workers start, the order cannot be safely recalled (no physical intervention mechanism).
- **Alternatives considered**: Cancel at any state — rejected (impossible to physically stop barista/kitchen in flight).

### D-009: `Others` Category Routing

- **Decision**: Kitchen (identical to `Food`).
- **Rationale**: FR-023. Simplifies routing logic; no special handler needed.

### D-010: Session Identity

- **Decision**: `SessionId = $"counter-{customerId}"` for stable, trackable sessions.
- **Rationale**: Copilot SDK cookbook recommends meaningful `SessionId`. Enables `ResumeSessionAsync` if needed.
- **Alternatives considered**: Random GUID — rejected (not trackable, no resume support).

---

## Gaps Identified (from full research report)

| Gap | Severity | Recommendation |
|-----|---------|----------------|
| No FR for valid state transitions | Low | Document in data-model.md; test all invalid transitions. |
| No FR for concurrent order uniqueness | Low | Integration test: two simultaneous POST /orders must get unique IDs. |
| No SC for streaming latency | Low | Use FR-020's "within 2 seconds" wording as implicit bound. |
| `in_preparation` still in User Story 3/5 | Low | Normalize to `preparing` during implementation; spec note added. |

---

## Confidence Summary

| Area | Level |
|------|-------|
| Aspire orchestration | HIGH |
| In-memory storage + seed data | HIGH |
| Channel<T> dispatch loop | HIGH |
| Clean architecture boundaries | HIGH |
| Integration testing with Aspire | HIGH |
| Observability (OTel + ServiceDefaults) | HIGH |
| MAF + Copilot SDK wiring | MEDIUM |
| AG-UI streaming fidelity (4 boundaries) | MEDIUM |
| MCP fail-fast error handling | MEDIUM |
| CopilotKit state sync | LOWER |
| Copilot CLI dependency in CI | LOWER |
