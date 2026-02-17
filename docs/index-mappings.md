# Elasticsearch Index Bootstrap

## Index Template: logs-*
- `source`: keyword
- `env`: keyword
- `correlationKey`: keyword
- `message`: text
- `timestampUtc`: date

## Index: incidents
- `correlationKey`: keyword
- `source`: keyword
- `env`: keyword
- `severity`: keyword
- `status`: keyword
- `anomalyScore`: double
- `firstSeenUtc`: date
- `lastSeenUtc`: date
- `explanation`: text

## Index Template: incident-events-*
- `incidentId`: keyword
- `correlationKey`: keyword
- `occurredAtUtc`: date
- `eventType`: keyword

## Dead letters
- index `dead-letters` with `DeadLetterMessage` payloads.
