# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: Backend: C# 13 / .NET 10 | Frontend: TypeScript (strict) / Next.js App Router | Orchestrator: .NET Aspire 13.x  
**Architecture**: 4-component clean architecture — counter (in-process, HTTP + agent), barista (in-process), kitchen (in-process), product-catalog (out-process MCP Server). counter↔barista/kitchen: in-process C# Channel; counter↔product-catalog: MCP HTTP/SSE.  
**Agent Framework**: Microsoft Agent Framework (`Microsoft.Agents.AI`) for counter, barista, kitchen. LLM provider: GitHub Copilot SDK via `copilotClient.AsAIAgent(sessionConfig, ownsClient: false, ...)` — no Azure credentials or GITHUB_TOKEN env var needed; Copilot CLI handles auth. Chat-client turn lifecycle also via Copilot SDK; patterns from `github/awesome-copilot/cookbook/copilot-sdk/dotnet` and `agent-framework/.../Agent_With_GitHubCopilot`.  
**Primary Dependencies**: Backend: Microsoft.Agents.AI, GitHub.Copilot.SDK (counter only), xUnit, WebApplicationFactory, OpenTelemetry.Exporter.OpenTelemetryProtocol (via ServiceDefaults) | Frontend: @copilotkit/react-core, @copilotkit/react-ui, shadcn UI, Tailwind CSS, Vitest, React Testing Library, @vercel/otel, @opentelemetry/sdk-web, @opentelemetry/exporter-trace-otlp-http | AppHost: Aspire.Hosting.NodeJs  
**Storage**: In-memory only — all domain state (customers, orders, menu items, session intent) is held in process-local singleton collections in the counter service. No external database, cache, or file persistence is permitted (Constitution VII).  
**Testing**: Unit: xUnit (backend) + Vitest/React Testing Library (frontend) | Integration: xUnit + Aspire.Hosting.Testing (DistributedApplicationTestingBuilder, shared tests/integration/ project)  
**Target Platform**: Web (local dev via Aspire AppHost; backend: Linux/Windows server; frontend: browser)  
**Project Type**: Web application (multi-component backend + Next.js frontend + Aspire AppHost)  
**Performance Goals**: [domain-specific, e.g., <200ms p95 API response or NEEDS CLARIFICATION]  
**Constraints**: Minimal dependencies (Constitution I); no Newtonsoft; no custom CSS unless Tailwind insufficient; no hard-coded localhost ports (use Aspire service discovery); no direct LLM HTTP calls (use MAF agent loop)  
**Scale/Scope**: [domain-specific, e.g., small coffeeshop or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

[Gates determined based on constitution file]

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
