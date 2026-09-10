namespace Portal.Application.Areas;

public sealed record AreaDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateAreaCommand(
    string? Name,
    string? Description);

public sealed record UpdateAreaCommand(
    string? Name,
    string? Description);

public enum AreaOperationStatus
{
    Success,
    NotFound,
    Invalid,
    Duplicate,
}

public sealed record AreaOperationResult(
    AreaOperationStatus Status,
    AreaDto? Area = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);
