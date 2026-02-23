import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
} from "@copilotkit/runtime";
import { HttpAgent } from "@ag-ui/client";

const counterUrl = process.env.COUNTER_URL ?? "http://localhost:5000";

// Single shared runtime instance — used by both the POST (chat) and GET
// (discovery) handlers.  Two separate instances would each open their own
// connection to the backend's AG-UI endpoint and duplicate every response.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const runtime = new CopilotRuntime({
  agents: {
    CoffeeShopCounter: new HttpAgent({
      url: `${counterUrl}/api/v1/copilotkit`,
    }),
  } as any,
});

// ExperimentalEmptyAdapter is required when the LLM lives inside the remote
// agent (GitHub Copilot CLI) — no LLM key needed on the Next.js side.
export const serviceAdapter = new ExperimentalEmptyAdapter();
