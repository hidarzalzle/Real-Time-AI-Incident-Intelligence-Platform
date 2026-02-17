using Application;

namespace Infrastructure;

public sealed class OpenAiLlmClient : ILLMClient
{
    public Task<LlmIncidentAnalysis> AnalyzeAsync(LlmIncidentRequest req, CancellationToken ct)
        => throw new NotImplementedException("OpenAI provider stub is wired for configuration-driven activation.");
}

public sealed class GeminiLlmClient : ILLMClient
{
    public Task<LlmIncidentAnalysis> AnalyzeAsync(LlmIncidentRequest req, CancellationToken ct)
        => throw new NotImplementedException("Gemini provider stub is wired for configuration-driven activation.");
}
