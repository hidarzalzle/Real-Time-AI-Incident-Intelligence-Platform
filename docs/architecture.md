# Architecture

```text
┌────────────────────────────┐
│ API / Simulator            │
│ POST /api/simulate/logs    │
└──────────────┬─────────────┘
               │ logs.exchange (logs.ingest)
        ┌──────▼───────┐
        │ RabbitMQ     │
        │ + DLQ        │
        └──────┬───────┘
               │ logs.ingest.q
        ┌──────▼───────────────────────────────────┐
        │ Worker                                   │
        │ - LogConsumerService                     │
        │ - AnomalyEngine (EWMA+burst+novelty)    │
        │ - LLM classification                     │
        │ - Redis idempotency + distributed lock   │
        │ - AutoResolveService                     │
        └──────┬───────────────────────────────────┘
               │ incidents.update
       ┌───────▼────────┐            ┌──────────────────┐
       │ Elasticsearch   │            │ API + SignalR     │
       │ logs-*,         │<-----------│ incident events    │
       │ incidents,      │            │ dashboard streams  │
       │ incident-events │            └──────────────────┘
       └────────────────┘
```

OpenTelemetry traces/metrics/logs are exported to `otel-collector` via OTLP.
