using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace DotNetClaw;

public static class ClawTelemetry
{
    public const string ActivitySourceName = "DotNetClaw.Agent";
    public const string MeterName = "DotNetClaw.Agent";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> RequestsTotal = Meter.CreateCounter<long>(
        "dotnetclaw_requests_total",
        unit: "{request}",
        description: "Total number of agent requests");

    public static readonly Counter<long> ErrorsTotal = Meter.CreateCounter<long>(
        "dotnetclaw_errors_total",
        unit: "{error}",
        description: "Total number of failed agent requests");

    public static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "dotnetclaw_request_duration_ms",
        unit: "ms",
        description: "End-to-end request duration in milliseconds");

    public static readonly Histogram<long> ResponseLengthChars = Meter.CreateHistogram<long>(
        "dotnetclaw_response_length_chars",
        unit: "{char}",
        description: "Assistant response size in characters");
}
