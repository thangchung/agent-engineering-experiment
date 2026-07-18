# Loop Runtime — Agent Instructions

You are a senior .NET engineer building a convergent multi-agent code-generation system.
Architecture, milestones, and decisions live in [plan.md](./plan.md). Requirements in [ema_xaa_reqs.md](./ema_xaa_reqs.md).

---

## Rules

**Language & Runtime**
- C# / .NET 10. Solution file = `.slnx`. All projects `net10.0`.
- No keys in code. Secrets via `dotnet user-secrets` (dev) or env vars (CI).
- Always use top-level-statements (https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/program-structure/top-level-statements)

**Principles**
- Red-Green-Refactor: write failing test first, make it pass, then clean up. No code without a test.
- Karpathy: simplest thing that passes verify. No speculative abstraction. Every line traces to a requirement.
- One concern per layer: business logic (MAF) / communication (A2A, MCP) / auth (EMA) / enforcement (AgentGateway) never bleed into each other.
- `ICodeSandbox` is the single execution boundary — RunPython always delegates there.
- Stop and ask before coding if spec is ambiguous. Surface assumptions first.

**Infrastructure**
- **Aspire AppHost** orchestrates ALL services (MCP server, Checker A2A, AgentGateway container). `aspire start` = only dev run command. Aspire handles startup order, service discovery, config injection, OTel dashboard.
- ServiceDefaults: `builder.AddServiceDefaults()` in every service — gives OTel traces/logs/metrics to Aspire dashboard for free.
- Sandbox: Docker (`python:3-slim`, `--network=none`, `--memory=256m`, `--pids-limit=64`, 10s wall kill). `ICodeSandbox` = extension point for future impls (Hyperlight etc.).
- Gateway: AgentGateway `v1.4.0-alpha.1` as Aspire container resource (`AddContainer` + `gateway.yaml` bind-mount) — config only, no app change.
- Auth Phase 4A: Okta + Auth0 XAA (ID-JAG, RFC 8693 + 7523). Phase 4B: Entra OBO (later).
- Model: Azure OpenAI via `IChatClient`. Fake impl for tests — must script tool calls, not text.
- OTel tracing (P2) via Aspire ServiceDefaults. Fake model + fake sandbox keep tests deterministic always.

**Versions** — locked to MAF `Directory.Packages.props@7ca73c0`. Never bump independently.
`MAF 1.13.0` · `Harness 1.13.0-preview.260703.1` · `MCP 1.2.0` · `A2A 1.0.0-preview2` · `Extensions.AI 10.6.0` · `Aspire.Hosting 13.4.6`

---

## Project Structure

```
loop-runtime/
├── AGENTS.md
├── plan.md
├── mock.html
├── LoopRuntime.slnx
│
├── src/
│   ├── LoopRuntime.Contracts/          # Shared kernel — no dependencies outward
│   │   ├── ICodeSandbox.cs             # Task<RunResult> RunAsync(string code, CT)
│   │   ├── IChecker.cs                 # Task<Verdict> ReviewAsync(sid, path, CT)
│   │   ├── Verdict.cs                  # record(bool Ok, Feedback?)
│   │   ├── Feedback.cs                 # record(string Reason, string[] Fixes)
│   │   └── RunResult.cs                # record(int ExitCode, string Stdout, string Stderr, bool TimedOut)
│   │
│   ├── LoopRuntime.Sandbox/            # ICodeSandbox impls
│   │   ├── DockerSandbox.cs            # P1 primary — Mac + Linux
│   │   └── HyperlightSandbox.cs        # Later phase — Linux/KVM only
│   │
│   ├── LoopRuntime.ServiceDefaults/    # Aspire ServiceDefaults — shared OTel + health checks
│   │   └── Extensions.cs               # AddServiceDefaults() + MapDefaultEndpoints()
│   │
│   ├── LoopRuntime.Mcp/                # Single MCP server, stdio transport
│   │   ├── McpServer.cs                # Host + tool registration
│   │   └── Tools/
│   │       ├── WriteFileTool.cs        # WriteFile(sid, name, content) → path
│   │       ├── EditFileTool.cs         # EditFile(sid, path, content) → path
│   │       ├── LoadFileTool.cs         # LoadFile(path) → content
│   │       └── RunPythonTool.cs        # RunPython(path) → RunResult via ICodeSandbox
│   │
│   ├── LoopRuntime.Agents/             # MAF orchestration
│   │   ├── ExecutorAgent.cs            # HarnessAgent wiring + IChatClient injection
│   │   ├── CheckerAgent.cs             # IChecker impl — loads file, runs sandbox, returns Verdict
│   │   ├── LoopOrchestrator.cs         # LoopAgent(executor, DelegateLoopEvaluator, MaxIterations=5)
│   │   ├── SessionState.cs             # Carries sessionId + path through ctx.Session
│   │   └── Fakes/
│   │       └── FakeChatClient.cs       # Scripts WriteFile tool call → done msg. Deterministic.
│   │
│   ├── LoopRuntime.Executor/           # Console entrypoint
│   │   ├── Program.cs                  # dotnet run -- "Write a Fibonacci program"
│   │   └── appsettings.json
│   │
│   ├── LoopRuntime.AppHost/            # Aspire AppHost — orchestrates all services + containers
│   │   ├── Program.cs                  # AddProject Host, Mcp, Checker.Service; AddContainer AgentGateway
│   │   └── gateway.yaml                # AgentGateway config (bind-mounted into container)
│   │
│   └── LoopRuntime.Checker/            # P3 only — A2A endpoint wrapping IChecker
│       ├── Program.cs
│       └── CheckerA2AEndpoint.cs
│
└── tests/
    ├── LoopRuntime.Tests/              # Deterministic: FakeChatClient + FakeSandbox
    │   ├── LoopOrchestratorTests.cs    # fibonacci pass, cap-5 stop, stall detect
    │   ├── CheckerAgentTests.cs        # exit-0 pass, exit-1 fail+feedback, timeout
    │   └── DockerSandboxTests.cs       # infinite loop kill, no-net, scratch-only
    └── LoopRuntime.Evals/              # P2 — golden tasks, real model, pass-rate baseline
        └── FibonacciEval.cs
```

---

## Checker Pass Rule

`Verdict.Ok = build ok AND exitCode == 0 AND (no expected output OR stdout matches expected)`
Fail → `Feedback.Reason` = first error line, `Fixes` = structured hints → `LoopEvaluation.Continue(feedback)`.
