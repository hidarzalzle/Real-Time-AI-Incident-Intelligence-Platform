using Application;

namespace Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public sealed class MockLlmClient : ILLMClient
{
    public Task<LlmIncidentAnalysis> AnalyzeAsync(LlmIncidentRequest req, CancellationToken ct)
    {
        var severity = req.AnomalyScore > 0.9 ? "S1" : req.AnomalyScore > 0.75 ? "S2" : "S3";
        var category = req.EvidenceMessages.Any(x => x.Contains("timeout", StringComparison.OrdinalIgnoreCase) || x.Contains("latency", StringComparison.OrdinalIgnoreCase))
            ? "Latency"
            : "Errors";
        return Task.FromResult(new LlmIncidentAnalysis(
            category,
            severity,
            $"{req.Source} incident detected in {req.Env}",
            "EWMA error baseline deviation and burst signals indicate likely service degradation.",
            new[] { "Validate dependent services", "Check deployment timeline", "Scale or rollback if needed" },
            0.91,
            "mock",
            "mock-deterministic-v2",
            64,
            128));
    }
}
