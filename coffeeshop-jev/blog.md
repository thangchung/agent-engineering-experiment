# Blog blueprint: "Prod, not god" on .NET: Jev + Microsoft Agent Framework + OpenTelemetry

> Skeleton only: highlights per section, for you to write up.
> Main reference: LangChain, *Building Prod with Jev and LangGraph*: https://www.langchain.com/blog/building-prod-with-jev-and-langgraph
> Speculative fan-out reference: Aman Arora, *Jev: A New Way to Make Probabilistic Decisions* §7: https://amaarora.github.io/posts/2026-19-09-jev-intro.html#speculative-fan-out

**Working titles**
- "Prod, not god" in .NET: a coffee shop on Jev + Microsoft Agent Framework
- Cheap by default, frontier on exception: decision models inside a MAF workflow

**One-line thesis:** The LangChain post shows the pattern on LangGraph. This post does the same on .NET with Microsoft Agent Framework (MAF) 1.22, and adds the piece they got from LangSmith: vendor-neutral tracing with the OpenTelemetry GenAI semantic conventions.

---

## 1. Hook: the "god model" tax
- Quote the LangChain framing: treating frontier LLMs "like god" for every fuzzy step, and paying for it "even when all you needed was a yes or no."
- TypeSafe's claim (via the article): up to 200x faster and 400x cheaper on narrow decision tasks. Frame it as a vendor benchmark, not ours.
- Our angle: most of a coffee order is bounded choices (intent, on-menu?, which station?). Only 3 steps really need generated text.

## 2. Jev in 60 seconds (keep it short)
- One endpoint, `POST /v1/systemone`. Three question types: `choice`, `score`, `noul`. The answer is a distribution plus confidence, never text.
- The 4 properties from the article: structured, parallel, fast, self-consistent. Map each one to where the coffee shop uses it.
- The key mental split: "Jev decides, the LLM writes." Jev can't extract, so the LLM turns "2 lattes + croissant" into lines.

## 3. Mapping LangGraph concepts to MAF
- Small table: LangGraph node / state / conditional edge / interrupt ↔ MAF `Executor` / `ReadOrInitStateAsync` in a named scope / `AddSwitch` / `RequestPort`.
- Echo the article's "state as context" and "encode domain knowledge in the topology, not the prompt". Our `OrderWorkflow.Build` is the topology.
- Diagram: reuse the executor-wiring mermaid from README.md (Gate → Extract/Clarify/ShowMenu/Reply → Split → Barista ‖ Kitchen → Deliver).

## 4. The practical build: where Jev sits in the workflow
- **Gate** (intent-routing + **speculative fan-out**): 1 Jev call with 2 questions (`intent` choice + `on_menu` noul). Threshold 0.5, taken from the docs. `IntentPolicy.Decide` is a pure function.

### 4a. Speculative fan-out at the Gate (own subsection)
- Define it with Arora's words (§7, a TypeSafe-recommended pattern): send every question you *might* need in one request, Jev evaluates them in parallel, and code keeps the relevant answers and ignores the rest.
- Ours: `on_menu` is only needed when `intent == place_order`. We ask it anyway, in the same call (`GateQuestions.Build`). `IntentPolicy.Decide` reads it only on the `place_order` branch. For `ask_menu`, `off_topic`, or low confidence, it's thrown away.
- Sequential alternative: ask intent → wait → if place_order, ask on_menu. That's 2 round trips on the hot path of every order. Speculative is always 1.
- Why it's cheap enough to waste answers: Jev scores questions in parallel against the same state, not token by token (Arora §2). So an extra question costs close to nothing in latency. The same move would be wasteful with an LLM.
- The "route with code" half (Arora §7.1): the model returns typed decisions, and the C# `switch` in `IntentPolicy` owns the routing. This is the same idea as the LangChain post's "flow stays in code you can inspect and test".
- Where it grows (research.md §11, designed and not shipped): the G1 hazard nouls (`hz_injection`, `hz_abuse`, …) ride along in the same Gate call, so there are 0 extra round trips. The same goes for the per-line grounding questions (G2) in the Split call.
- Contrast to call out: the Split call batches N `station_i` questions too, but it uses every answer. That's batching, not speculation. Show the Gate as speculative and the Split as parallel batching.
- Naming callout: don't confuse this with MAF's `AddFanOutEdge(split, [barista, kitchen])`. That fans out *executors* (graph topology). Speculative fan-out fans out *questions* inside one Jev request.
- Snippet: `GateQuestions.Build` (2 questions) side by side with Arora's support-ticket example (5 questions, category-gated reads).
- **Split** (confidence-routing): 1 Jev call, one `station_i` question per line. Bands 0.6 / 0.85 → None / Confirm / Review. Flags go to staff, never to the customer.
- **Human in the loop**: `RequestPort` "ask-customer", the counterpart of the article's `attorney_review` interrupt. Capped at 2 asks.
- **Fail-closed vs fail-open**: Jev down at the Gate → reject ("try again"). Jev down at the Split → everything to Kitchen and flagged Review, so the order still flows.
- **Gotcha worth a callout**: the MAF fan-in barrier hangs unless every station always emits. Hence `StationTicket.Empty` / `Fallback`.
- Code snippets to include (short): `OrderWorkflow.Build` (~15 lines), `IntentPolicy.Decide`, `StationPolicy.Assign`.

## 5. Observability: GenAI semantic conventions for a model that doesn't generate
- The problem: LangSmith has dedicated views for decision models. On .NET/Aspire we want the same thing without vendor lock-in.
- `Jev.Client` emits its own `ActivitySource("Jev.Client")` spans tagged with the same `gen_ai.*` family the OpenAI calls carry: `gen_ai.operation.name`, `gen_ai.provider.name=openjev`, `gen_ai.request/response.model`, `gen_ai.usage.*_tokens`, `server.*`, `error.type`.
- `gen_ai.input.messages` / `gen_ai.output.messages` are opt-in (sensitive data, dev only). They hold the raw distributions, so you can "open any order and see the route and the probabilities behind it", as the article puts it.
- Result: Jev reads and Foundry chat calls sit side by side in one Aspire GenAI trace view (`chat jev-latest` next to `chat <model>`).
- MAF side: `UseOpenTelemetry` on all 3 agents. ServiceDefaults adds the sources `Microsoft.Agents.AI.Workflows*`, `CoffeeShop.Agents`, `Jev.Client` and the MCP source.
- Custom workflow events `GateDecided` / `SplitDone` carry the decision for the UI and the evals.
- Honest note: planned `jev.intent` / `jev.confidence` span tags are **not** in the code yet. Decisions are visible via the `gen_ai.*.messages` payloads and the events. Possible follow-up or teaser.
- Screenshot slot: Aspire dashboard trace of one order (Gate → Extract → Split → 2 stations → Deliver).

## 6. Jev as a judge (evals)
- The article's claim: Jev-as-a-judge scores "barely moved across 100 repeated runs." We reproduced it on our own rubric: variance 2.4e-6 to 1.9e-5 over 30 reps × 3 tickets, the same order of magnitude as the danielgshea/jev-as-a-judge benchmark.
- 3-tier cascade: Jev accepts when confidence is ≥ 0.6, otherwise escalate to the LLM judge, then a human. That is "cheap by default, frontier on exception" applied to evals.
- Rule to highlight: deterministic checks gate pass/fail (price, exact lines, no dropped items). Judges only score soft qualities (tone, realism).

## 7. Benefits in *this* project (the numbers section)
- **1 round trip at the Gate, however many questions**: speculative fan-out keeps the Gate at a single Jev call now (intent + on_menu) and later (+ hazard guards).
- **Fewer LLM calls**: Jev handles 2 of the decision points (Gate, Split). The LLM only does extract, clarify, 2 tickets and the reply, and an empty station skips its LLM call.
- **Deterministic, testable routing**: the policies are pure functions and the thresholds are unit-tested at the band edges, with no network needed.
- **Calibrated uncertainty drives UX**: low confidence becomes "ask the customer" (Gate) or a "staff badge" (Split), instead of a silent guess.
- **First live baseline (1 run, not locked)**: gate accuracy 81.5% (22/27), false-accept 0, station accuracy 100% (22/22), confident-wrong 0.
- Where the gate missed: typos ("expresso and a crossant"), paraphrases ("coffee with room for milk"), and turn-to-turn "hmm/idk". None was safety-relevant. It's a good "what Jev isn't great at" paragraph.
- Contrast with the article's numbers (Stagehand 1.97s → 0.46s; 5–6x faster than a Sonnet judge on classification). We don't have a head-to-head latency number yet. Either add a TODO measurement or say so plainly.

## 8. Trade-offs / when not to use Jev
- The closed 11-item catalog is demo value; a static `station` column would do the same. Jev earns its keep on free-text items.
- It needs its own serving (OpenJev on a GPU host) next to the LLM endpoint: two things to operate.
- The in-memory human-wait is single-instance only. Checkpointing is the upgrade path.
- Guardrails (hazard nouls, grounding, output guard) are designed in research.md §11 but not shipped. Optional teaser for part 2.

## 9. Close
- Tie back to the "great unbundling of intelligence": the runtime (LangGraph there, MAF here) stitches cheap decisions and expensive generation together.
- Takeaway checklist: find the bounded choices → one Jev call with N questions, asked speculatively → a pure policy function with doc thresholds → escalate on low confidence → trace everything with `gen_ai.*`.
- Links: repo, research.md, the LangChain post, Arora's Jev intro (speculative fan-out §7), Jev docs (docs.typesafe.ai), OTel GenAI semconv.

---

**Assets to prepare:** executor-wiring mermaid (README), Aspire trace screenshot, `report.md` table, 3 short code snippets.
**Don't publish:** research.md §10.9 contains a real OpenJev IP and an internal endpoint hostname. Scrub them before quoting.
