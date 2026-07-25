namespace IndustrialVisionStudent.Models;

public sealed record AuditEvent(
    DateTimeOffset Timestamp,
    string Action,
    string Outcome,
    string Details);
