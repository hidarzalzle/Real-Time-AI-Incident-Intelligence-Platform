using Application;
using Domain;
using Xunit;

public class AnomalyEngineTests
{
    [Fact]
    public void HighErrorBurst_ProducesHighScore()
    {
        var engine = new AnomalyEngine();
        var state = WindowState.Empty;
        var log = new LogEvent(Guid.NewGuid(), "checkout-api", "prod", LogLevel.Error, "Payment timeout", DateTime.UtcNow, null, null, "node-1", new Dictionary<string,string>(), "fp1", "k1", "m1", "i1");
        var score = engine.Evaluate(log, state);
        Assert.True(score.Score >= 0.2);
    }
}
