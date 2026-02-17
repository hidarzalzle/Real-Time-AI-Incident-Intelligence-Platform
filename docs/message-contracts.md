# Message Contracts

## Envelope
All RabbitMQ messages use a shared envelope:

```json
{
  "messageId": "string",
  "occurredAtUtc": "2026-02-17T12:00:00Z",
  "correlationId": "checkout-api:prod:...",
  "idempotencyKey": "string",
  "payload": {}
}
```

## Routes
- Exchange `logs.exchange` + key `logs.ingest` -> queue `logs.ingest.q` (+ DLQ `logs.ingest.dlq`)
- Exchange `incidents.exchange` + key `incidents.update` -> queue `incidents.update.q` (+ DLQ `incidents.update.dlq`)

## Payloads
- `LogEvent` in `logs.ingest`
- `IncidentLifecycleEvent` in `incidents.update` (`incidentOpened`, `incidentUpdated`, `incidentResolved`, `deadLettered`)
