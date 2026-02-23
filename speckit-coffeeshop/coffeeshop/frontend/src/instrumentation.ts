/**
 * OpenTelemetry initialisation for the CoffeeShop frontend.
 *
 * Loaded by Next.js before the application code runs when
 * `experimental.instrumentationHook: true` is set in next.config.ts.
 *
 * Instruments:
 *   - Page load / navigation spans
 *   - fetch() calls to /api/copilotkit (AG-UI streaming)
 *   - fetch() calls to counter REST endpoints
 *
 * OTLP endpoint is Aspire-injected via OTEL_EXPORTER_OTLP_ENDPOINT.
 * Do NOT hardcode a localhost URL here.
 */

export async function register() {
  // Only run OTel initialisation in the Node.js runtime (server-side).
  // The browser bundle should NOT include the OTLP exporter.
  if (process.env.NEXT_RUNTIME === "nodejs") {
    const { registerOTel } = await import("@vercel/otel");
    const {
      OTLPTraceExporter,
    } = await import("@opentelemetry/exporter-trace-otlp-http");
    const {
      BatchSpanProcessor,
    } = await import("@opentelemetry/sdk-trace-base");

    const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;

    const spanProcessors = otlpEndpoint
      ? [
          new BatchSpanProcessor(
            new OTLPTraceExporter({
              url: `${otlpEndpoint}/v1/traces`,
            })
          ),
        ]
      : [];

    registerOTel({
      serviceName: "coffeeshop-frontend",
      spanProcessors,
    });
  }
}
