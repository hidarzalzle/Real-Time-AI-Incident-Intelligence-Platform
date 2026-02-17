using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Application;

public static class PlatformTelemetry
{
    public static readonly ActivitySource ActivitySource = new("RealTimeIncident.Platform");
    public static readonly Meter Meter = new("RealTimeIncident.Platform", "1.0.0");

    public static readonly Counter<long> LogsProcessed = Meter.CreateCounter<long>("logs_processed_total");
    public static readonly Counter<long> IncidentsOpened = Meter.CreateCounter<long>("incidents_open_total");
    public static readonly Counter<long> IncidentsResolved = Meter.CreateCounter<long>("incidents_resolved_total");
    public static readonly Counter<long> LlmCalls = Meter.CreateCounter<long>("llm_calls_total");
    public static readonly Counter<long> DlqTotal = Meter.CreateCounter<long>("dlq_total");
    public static readonly Histogram<double> ProcessingDurationMs = Meter.CreateHistogram<double>("processing_duration_ms");
}
