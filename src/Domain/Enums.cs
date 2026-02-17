namespace Domain;

public enum IncidentSeverity { S0, S1, S2, S3, S4 }
public enum IncidentCategory { Latency, Errors, Availability, Security, Cost, Unknown }
public enum IncidentStatus { Open, Investigating, Mitigated, Resolved, FalsePositive, DeadLettered }
public enum LogLevel { Trace, Debug, Information, Warning, Error, Critical }
