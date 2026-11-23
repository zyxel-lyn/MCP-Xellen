using System.Text.Json.Serialization;

namespace McpXellen.Core;

public enum ActivityStatus { Queued, Running, Success, Failed, Cancelled, Timeout }

/// <summary>
/// One observable AI/tool activity record (PRD §9). Secrets are never stored: summaries pass
/// through Redaction before construction, and payload size is bounded by the producer.
/// </summary>
public sealed class ActivityEvent
{
    [JsonPropertyName("eventId")] public Guid EventId { get; init; } = Guid.NewGuid();
    [JsonPropertyName("correlationId")] public Guid CorrelationId { get; init; } = Guid.NewGuid();
    [JsonPropertyName("timestampUtc")] public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    [JsonPropertyName("eventType")] public string EventType { get; init; } = "tool_call";
    [JsonPropertyName("clientId")] public string? ClientId { get; init; }
    [JsonPropertyName("projectId")] public string? ProjectId { get; init; }
    [JsonPropertyName("serverId")] public string? ServerId { get; init; }
    [JsonPropertyName("toolName")] public string? ToolName { get; init; }
    [JsonPropertyName("status")] public ActivityStatus Status { get; set; } = ActivityStatus.Queued;
    [JsonPropertyName("durationMs")] public long DurationMs { get; set; }
    [JsonPropertyName("riskLevel")] public string RiskLevel { get; set; } = "normal";
    [JsonPropertyName("inputSummary")] public string? InputSummary { get; set; }
    [JsonPropertyName("outputSummary")] public string? OutputSummary { get; set; }
    [JsonPropertyName("errorCode")] public string? ErrorCode { get; set; }
    [JsonPropertyName("processId")] public int? ProcessId { get; set; }
}
