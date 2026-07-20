using System.Text.Json;

public sealed record SayRequest(string Text);

public sealed record NotificationEventRequest(
    string? Source,
    string? EventType,
    string? Severity,
    string? Emotion,
    string? Message,
    string? Summary,
    string? DedupeKey,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? ExpiresAt,
    string? StateKey,
    string? State,
    Dictionary<string, JsonElement>? Metadata);

public sealed record NotificationEnvelope(
    long Id,
    string Source,
    string EventType,
    string Severity,
    string Emotion,
    string Message,
    string DedupeKey,
    string? StateKey,
    string? State,
    DateTimeOffset CreatedAt);

public sealed record EventDecision(bool Accepted, string Outcome, string? Reason, long? EventId = null);
public sealed record DeliveryResult(bool Success, string? Error = null);
public sealed record ReadinessStatus(bool StorageAvailable, bool VectorReachable);
