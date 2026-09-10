namespace Portal.Application.Auditing;

public sealed record AuditActorDto(
    Guid Id,
    string Name);

public sealed record AuditEventDto(
    Guid Id,
    string Action,
    string? EntityType,
    string? EntityId,
    string Result,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId,
    AuditActorDto? Actor,
    IReadOnlyDictionary<string, string>? Metadata,
    string? UserAgent);

public sealed record AuditPageDto(
    IReadOnlyList<AuditEventDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record AuditQuery(
    int Page,
    int PageSize,
    string? Action,
    string? EntityType,
    string? Result,
    Guid? ActorUserId,
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    string? Search);
