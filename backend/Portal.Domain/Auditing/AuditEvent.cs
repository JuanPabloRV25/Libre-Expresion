using System.Net;

namespace Portal.Domain.Auditing;

public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public Guid? ActorUserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public string Result { get; set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; set; }

    public IPAddress? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    public Guid? CorrelationId { get; set; }

    public string? Metadata { get; set; }
}
