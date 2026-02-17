# Real-Time AI Incident Intelligence Platform (.NET 8)

A production-style, portfolio-grade SaaS platform that ingests live logs, detects anomalies, enriches incidents with LLM reasoning, indexes state in Elasticsearch, and streams updates to a SignalR dashboard.

## Product pitch
This project demonstrates modern platform engineering with event-driven design, reliability patterns (idempotency, distributed locks, DLQ), observability (OpenTelemetry), and a demo-ready live dashboard.

## Architecture

```text
POST /api/simulate/logs or external producer
    -> RabbitMQ logs.exchange (logs.ingest)
    -> Worker LogConsumerService
    -> AnomalyEngine (EWMA + burst + novelty + host spread)
    -> LLMClient (mock deterministic / OpenAI / Gemini stubs)
    -> IncidentRepository + Elasticsearch (logs, incidents, incident-events)
    -> RabbitMQ incidents.exchange (incidents.update)
    -> API IncidentUpdateForwarder -> SignalR hub
    -> /dashboard live updates

AutoResolveService scans stale incidents and publishes incidentResolved events.
Failures are dead-lettered and indexed in dead-letters + surfaced on dashboard.
```

## Anomaly scoring
The worker computes sliding-window signals per `correlationKey`:
- EWMA baseline error-rate and variance
- burst ratio (`currentRate / baselineRate`)
- novelty score (new fingerprint in active window)
- host spread score (cross-host propagation)

Final score:

`score = normalize(0.4*z_error + 0.25*burst + 0.2*novelty + 0.15*hostSpread)`

## LLM enrichment
`ILLMClient` receives title/source/env/anomaly score/metrics/evidence and returns:
- category + severity
- summary + human explanation
- recommended next actions

`MockLlmClient` is deterministic for tests and demo reliability.

## Quickstart
```bash
cp .env.example .env
docker compose up --build
```

### Endpoints
- Dashboard: `http://localhost:8080/dashboard`
- Swagger: `http://localhost:8080/swagger`
- Health: `http://localhost:8080/health`
- RabbitMQ: `http://localhost:15672`
- Elasticsearch: `http://localhost:9200`
- Kibana: `http://localhost:5601`

## Demo steps
1. Open dashboard.
2. Trigger one of the scenarios.
3. Watch incidents appear and update in real time.
4. Click an incident row for LLM summary/explanation/actions and evidence.
5. Inspect DLQ panel.

## Observability
- Spans: `log.process`, `incident.upsert`, `llm.analyze`, `elastic.index`
- Metrics: `logs_processed_total`, `incidents_open_total`, `incidents_resolved_total`, `llm_calls_total`, `dlq_total`, `processing_duration_ms`
- Exported via OTLP to collector (`otel-collector` service). Console exporters are enabled in Development.

## Testing
```bash
dotnet test RealTimeIncidentIntelligence.sln
```

## Roadmap
- Rule DSL + dynamic reload
- outbox persistence for exactly-once publication semantics
- multi-tenant isolation and suppression windows
- PagerDuty/Opsgenie integrations
