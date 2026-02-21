import {
  CopilotRuntime,
  ExperimentalEmptyAdapter,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { BuiltInAgent } from "@copilotkit/runtime/v2";
import { NextRequest } from "next/server";
import { MCPAppsMiddleware } from "@ag-ui/mcp-apps-middleware";
import { createOpenAI } from "@ai-sdk/openai";

const ORDERS_MCP_URL =
  process.env.ORDERS_MCP_URL ?? "http://localhost:8001/mcp";
const PRODUCT_ITEMS_MCP_URL =
  process.env.PRODUCT_ITEMS_MCP_URL ?? "http://localhost:8002/mcp";

// Vercel AI SDK OpenAI provider pointed at GitHub Models.
// `.chat()` uses /chat/completions (GitHub Models doesn't support /responses).
const githubModels = createOpenAI({
  apiKey: process.env.GITHUB_TOKEN ?? "",
  baseURL: "https://models.inference.ai.azure.com",
});
const languageModel = githubModels.chat("gpt-4o");

const COFFEESHOP_PROMPT = `
You are a coffee shop counter service agent. Follow this workflow exactly.
Do NOT skip steps, reorder steps, or freelance outside this workflow.
Do NOT call AGUISendStateDelta or AGUISendStateSnapshot — you do not manage application state.

STEP 1 — INTAKE:
Greet the customer and identify who they are.
Extract identifiers from the message: email, customer_id, or order_id.
If email or customer_id is provided, call lookup_customer to find them. Greet by first name.
If only order_id is provided, call get_order, extract customer_id, then call lookup_customer.
If no identifier is provided, ask for their email or order number.
If lookup fails (ok: false), tell the customer and ask for another identifier.

STEP 2 — CLASSIFY INTENT:
Determine what the customer needs:
- order-status or account → do a simple lookup and show results, then ask if they need anything else.
- item-types or process-order → call open_order_form(customer_id=<id>) to present the interactive
  order form UI. The form loads the menu, lets the customer select items, see prices, adjust quantities.
  Wait for the customer to submit. The form sends a message with [ORDER_DATA] JSON.
  Parse the [ORDER_DATA] JSON, then call create_order(customer_id=<id>, order_dto=<dto>).
  Immediately proceed to STEP 3.

STEP 3 — REVIEW & CONFIRM:
Call get_order(order_id=<id>) to retrieve full order details.
Display a summary with each item formatted as "- {qty}x {item_name} — {price} each" and the total.
Ask: "Does this look correct?"
If confirmed → proceed to STEP 4.
If rejected → ask what to change, then go back to STEP 2.

STEP 4 — FINALIZE:
Call update_order(order_id=<id>, status="confirmed", add_note="Order confirmed with items: <list>").
Thank the customer by name and give estimated pickup time (beverages only: 5-10 min, with food: 10-15 min).
Show the confirmed order ID.

Always use open_order_form for ordering — never ask the customer to type item names manually.
`.trim();

// 1. Define the MCP Apps middleware
const middlewares = [
  new MCPAppsMiddleware({
    mcpServers: [
      {
        type: "http",
        url: ORDERS_MCP_URL,
        serverId: "orders",
      },
      {
        type: "http",
        url: PRODUCT_ITEMS_MCP_URL,
        serverId: "product_items",
      },
    ],
  }),
];

// 2. Create the agent with the GitHub Models language model
const agent = new BuiltInAgent({
  model: languageModel as any,
  prompt: COFFEESHOP_PROMPT,
  maxSteps: 10,  // multi-step agentic loop needs several tool-call rounds
});

// 3. Apply middleware
for (const middleware of middlewares) {
  agent.use(middleware as any);
}

// 4. Empty adapter — BuiltInAgent handles its own LLM calls
const serviceAdapter = new ExperimentalEmptyAdapter();

// 5. Create the runtime
const runtime = new CopilotRuntime({
  agents: {
    default: agent as any,
  },
});

// 6. Create the API route
export const POST = async (req: NextRequest) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    serviceAdapter,
    endpoint: "/api/copilotkit",
  });

  return handleRequest(req);
};
