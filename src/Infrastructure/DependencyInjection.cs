using Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RabbitOptions>(configuration.GetSection(RabbitOptions.Section));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.Section));
        services.Configure<ElasticOptions>(configuration.GetSection(ElasticOptions.Section));
        services.Configure<ProcessingOptions>(configuration.GetSection(ProcessingOptions.Section));

        services.AddHttpClient<IElasticClient, ElasticClient>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAnomalyEngine, AnomalyEngine>();
        services.AddSingleton<RedisConnectionFactory>();
        services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
        services.AddSingleton<ILockManager, RedisLockManager>();
        services.AddSingleton<IWindowStateStore, RedisWindowStateStore>();
        services.AddSingleton<IIncidentRepository, IncidentRepository>();
        services.AddSingleton<IMessageBus, RabbitMqBus>();

        services.AddSingleton<MockLlmClient>();
        services.AddSingleton<OpenAiLlmClient>();
        services.AddSingleton<GeminiLlmClient>();
        services.AddSingleton<ILLMClient>(sp =>
        {
            var provider = configuration["LLM:Provider"]?.ToLowerInvariant();
            return provider switch
            {
                "openai" => sp.GetRequiredService<OpenAiLlmClient>(),
                "gemini" => sp.GetRequiredService<GeminiLlmClient>(),
                _ => sp.GetRequiredService<MockLlmClient>()
            };
        });

        services.AddSingleton<IncidentProcessor>();
        return services;
    }

    public static IHostApplicationBuilder AddPlatformObservability(this IHostApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration["OpenTelemetry:Endpoint"] ?? "http://otel-collector:4317";

        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                t.AddSource(PlatformTelemetry.ActivitySource.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));

                if (builder.Environment.IsDevelopment()) t.AddConsoleExporter();
            })
            .WithMetrics(m =>
            {
                m.AddMeter(PlatformTelemetry.Meter.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));

                if (builder.Environment.IsDevelopment()) m.AddConsoleExporter();
            });

        builder.Logging.AddOpenTelemetry(o =>
        {
            o.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
            if (builder.Environment.IsDevelopment()) o.AddConsoleExporter();
        });

        return builder;
    }
}
