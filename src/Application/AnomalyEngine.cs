using Domain;

namespace Application;

public interface IAnomalyEngine
{
    AnomalyScore Evaluate(LogEvent log, WindowState state);
    WindowState Next(WindowState current, LogEvent log);
}

public sealed record WindowState(double EwmaErrorRate, double Variance, int TotalCount, int ErrorCount, HashSet<string> Hosts, HashSet<string> Fingerprints)
{
    public static WindowState Empty => new(0.01, 0.1, 0, 0, new(), new());
}

public sealed class AnomalyEngine : IAnomalyEngine
{
    private const double Alpha = 0.2;
    public AnomalyScore Evaluate(LogEvent log, WindowState state)
    {
        var total = Math.Max(state.TotalCount + 1, 1);
        var errors = state.ErrorCount + (log.Level >= LogLevel.Error ? 1 : 0);
        var errorRate = (double)errors / total;
        var variance = Math.Max(state.Variance, 0.0001);
        var z = Math.Max(0, (errorRate - state.EwmaErrorRate) / Math.Sqrt(variance));
        var burst = state.EwmaErrorRate <= 0.0001 ? errorRate : errorRate / state.EwmaErrorRate;
        var novelty = state.Fingerprints.Contains(log.Fingerprint) ? 0.0 : 1.0;
        var hostSpread = state.Hosts.Contains(log.Host ?? "unknown") ? 0.2 : 1.0;
        var score = (0.4 * z) + (0.25 * Math.Min(burst, 5)) + (0.2 * novelty) + (0.15 * hostSpread);
        score = Math.Clamp(score / 4.0, 0, 1);
        return new(score, errorRate, burst, novelty, hostSpread);
    }

    public WindowState Next(WindowState current, LogEvent log)
    {
        var nextTotal = current.TotalCount + 1;
        var nextErrorCount = current.ErrorCount + (log.Level >= LogLevel.Error ? 1 : 0);
        var currentErrorRate = (double)nextErrorCount / Math.Max(nextTotal, 1);
        var ewma = (Alpha * currentErrorRate) + ((1 - Alpha) * current.EwmaErrorRate);
        var variance = (Alpha * Math.Pow(currentErrorRate - ewma, 2)) + ((1 - Alpha) * current.Variance);
        var hosts = new HashSet<string>(current.Hosts) { log.Host ?? "unknown" };
        var fp = new HashSet<string>(current.Fingerprints) { log.Fingerprint };
        return new(ewma, variance, nextTotal, nextErrorCount, hosts, fp);
    }
}
