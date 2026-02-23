import { copilotRuntimeNextJSAppRouterEndpoint } from "@copilotkit/runtime";
import { NextRequest } from "next/server";
import { runtime, serviceAdapter } from "./runtime";

export const maxDuration = 60;

// Instantiate the endpoint once at module level.
// Calling copilotRuntimeNextJSAppRouterEndpoint() per-request would invoke
// runtime.handleServiceAdapter() on every POST, wrapping the agents Promise
// in an ever-growing chain and risking duplicate event registrations.
const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
  runtime,
  serviceAdapter,
  endpoint: "/api/copilotkit",
});

/**
 * The GitHub Copilot SDK (Microsoft.Agents.AI) always appends ONE final
 * TEXT_MESSAGE_CONTENT event whose `delta` equals the fully assembled message
 * text — a "summary" snapshot, not a new token increment.  CopilotKit appends
 * every delta it receives, so the summary causes the whole message to appear
 * twice in the UI.
 *
 * This transform reads the SSE stream line-by-line and drops any
 * TEXT_MESSAGE_CONTENT event whose `delta` equals the running accumulation for
 * that message ID (i.e. it's the redundant summary, not a new chunk).
 */
function deduplicateAssistantDeltas(response: Response): Response {
  if (!response.body) return response;

  const accumulated = new Map<string, string>();
  const decoder = new TextDecoder();
  const encoder = new TextEncoder();
  let lineBuffer = "";

  const transform = new TransformStream<Uint8Array, Uint8Array>({
    transform(chunk, controller) {
      // Next.js App Router streams can deliver chunks as Uint8Array, Buffer,
      // or even plain strings depending on the Node.js version / polyfill.
      // Normalise to string before processing.
      if (typeof chunk === "string") {
        lineBuffer += chunk;
      } else if (Buffer.isBuffer(chunk)) {
        lineBuffer += (chunk as Buffer).toString("utf8");
      } else {
        lineBuffer += decoder.decode(chunk, { stream: true });
      }
      const parts = lineBuffer.split("\n");
      // Keep the last possibly-incomplete segment for the next chunk.
      lineBuffer = parts.pop() ?? "";

      const outLines: string[] = [];
      let skipNextBlank = false;

      for (const line of parts) {
        // After dropping a data line we also drop the blank SSE separator.
        if (skipNextBlank) {
          skipNextBlank = false;
          if (line === "") continue;
        }

        if (!line.startsWith("data: ")) {
          outLines.push(line);
          continue;
        }

        let event: Record<string, unknown>;
        try {
          event = JSON.parse(line.slice(6));
        } catch {
          outLines.push(line);
          continue;
        }

        if (event.type === "TEXT_MESSAGE_CONTENT") {
          const msgId = event.messageId as string;
          const delta = (event.delta as string) ?? "";
          const acc = accumulated.get(msgId) ?? "";

          // `delta === acc` means this event is the final "summary" copy of
          // the whole message text — identical to what we already streamed.
          if (delta === acc) {
            skipNextBlank = true;
            continue; // drop the duplicate
          }

          accumulated.set(msgId, acc + delta);
        } else if (event.type === "TEXT_MESSAGE_END") {
          accumulated.delete(event.messageId as string);
        }

        outLines.push(line);
      }

      if (outLines.length > 0) {
        // Rejoin with \n and restore the trailing newline consumed by split.
        controller.enqueue(encoder.encode(outLines.join("\n") + "\n"));
      }
    },
    flush(controller) {
      if (lineBuffer) {
        controller.enqueue(encoder.encode(lineBuffer));
      }
    },
  });

  return new Response(response.body.pipeThrough(transform), {
    status: response.status,
    statusText: response.statusText,
    headers: response.headers,
  });
}

export const POST = async (req: NextRequest) => {
  const res = await handleRequest(req);
  return deduplicateAssistantDeltas(res);
};

// The CopilotKit browser SDK sends GET requests to the same runtimeUrl when
// it performs agent discovery (e.g. listing available agents on page load).
export const GET = (req: NextRequest) => handleRequest(req);


