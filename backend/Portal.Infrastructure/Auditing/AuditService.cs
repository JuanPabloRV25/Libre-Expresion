using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Portal.Application.Auditing;
using Portal.Domain.Auditing;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Auditing;

public sealed class AuditService(ApplicationDbContext dbContext) : IAuditService
{
    public const int DefaultPageSize = 25;
    public const int MaximumPageSize = 100;

    private const int MaximumPage = 1_000_000;
    private const int MaximumMetadataValueLength = 500;
    private const int MaximumUserAgentLength = 500;

    private static readonly IReadOnlyDictionary<string, string> SafeMetadataKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "name",
            ["PreviousName"] = "previousName",
            ["IsActive"] = "isActive",
            ["RoleIds"] = "roleIds",
            ["AreaId"] = "areaId",
            ["Email"] = "email",
            ["PreviousEmail"] = "previousEmail",
            ["NotificationStatus"] = "notificationStatus",
            ["AddedRoleIds"] = "addedRoleIds",
            ["RemovedRoleIds"] = "removedRoleIds",
            ["AddedCodes"] = "addedCodes",
            ["RemovedCodes"] = "removedCodes",
        };

    public async Task<AuditPageDto> ListAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        var page = Math.Clamp(query.Page, 1, MaximumPage);
        var pageSize = Math.Clamp(query.PageSize, 1, MaximumPageSize);
        var auditEvents = ApplyFilters(
            dbContext.AuditEvents.AsNoTracking(),
            query);
        var totalItems = await auditEvents.CountAsync(cancellationToken);
        var skip = (page - 1) * pageSize;

        var rows = await (
            from auditEvent in auditEvents
            join actor in dbContext.Users.AsNoTracking()
                on auditEvent.ActorUserId equals (Guid?)actor.Id into actors
            from actor in actors.DefaultIfEmpty()
            orderby auditEvent.OccurredAt descending, auditEvent.Id descending
            select new AuditRow(
                auditEvent.Id,
                auditEvent.Action,
                auditEvent.EntityType,
                auditEvent.EntityId,
                auditEvent.Result,
                auditEvent.OccurredAt,
                auditEvent.CorrelationId,
                auditEvent.Metadata,
                auditEvent.UserAgent,
                actor == null ? null : actor.Id,
                actor == null ? null : actor.FirstName,
                actor == null ? null : actor.LastName))
            .Skip(skip)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var items = rows
            .Select(row => new AuditEventDto(
                row.Id,
                row.Action,
                row.EntityType,
                row.EntityId,
                row.Result,
                row.OccurredAt,
                row.CorrelationId,
                BuildActor(row),
                ToSafeMetadata(row.Metadata),
                Truncate(row.UserAgent, MaximumUserAgentLength)))
            .ToArray();

        var totalPages = totalItems == 0
            ? 0
            : (int)Math.Ceiling(totalItems / (double)pageSize);

        return new AuditPageDto(
            items,
            page,
            pageSize,
            totalItems,
            totalPages);
    }

    private static IQueryable<AuditEvent> ApplyFilters(
        IQueryable<AuditEvent> query,
        AuditQuery filters)
    {
        if (!string.IsNullOrWhiteSpace(filters.Action))
        {
            var action = filters.Action.Trim().ToLower();
            query = query.Where(auditEvent => auditEvent.Action.ToLower() == action);
        }

        if (!string.IsNullOrWhiteSpace(filters.EntityType))
        {
            var entityType = filters.EntityType.Trim().ToLower();
            query = query.Where(auditEvent =>
                auditEvent.EntityType != null
                && auditEvent.EntityType.ToLower() == entityType);
        }

        if (!string.IsNullOrWhiteSpace(filters.Result))
        {
            var result = filters.Result.Trim().ToLower();
            query = query.Where(auditEvent => auditEvent.Result.ToLower() == result);
        }

        if (filters.ActorUserId.HasValue)
        {
            query = query.Where(auditEvent =>
                auditEvent.ActorUserId == filters.ActorUserId.Value);
        }

        if (filters.DateFrom.HasValue)
        {
            query = query.Where(auditEvent =>
                auditEvent.OccurredAt >= filters.DateFrom.Value);
        }

        if (filters.DateTo.HasValue)
        {
            query = query.Where(auditEvent =>
                auditEvent.OccurredAt <= filters.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(filters.Search))
        {
            var search = filters.Search.Trim().ToLower();
            query = query.Where(auditEvent =>
                auditEvent.Action.ToLower().Contains(search)
                || (auditEvent.EntityType != null
                    && auditEvent.EntityType.ToLower().Contains(search))
                || (auditEvent.EntityId != null
                    && auditEvent.EntityId.ToLower().Contains(search)));
        }

        return query;
    }

    private static AuditActorDto? BuildActor(AuditRow row)
    {
        if (!row.ActorId.HasValue)
        {
            return null;
        }

        var name = $"{row.ActorFirstName} {row.ActorLastName}".Trim();
        return new AuditActorDto(
            row.ActorId.Value,
            string.IsNullOrWhiteSpace(name) ? "Usuario" : name);
    }

    private static IReadOnlyDictionary<string, string>? ToSafeMetadata(
        string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var safeMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!SafeMetadataKeys.TryGetValue(property.Name, out var safeKey))
                {
                    continue;
                }

                var value = ReadSafeValue(property.Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    safeMetadata[safeKey] = Truncate(
                        value,
                        MaximumMetadataValueLength)!;
                }
            }

            return safeMetadata.Count == 0 ? null : safeMetadata;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadSafeValue(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Array => ReadSafeArray(value),
            _ => null,
        };

    private static string? ReadSafeArray(JsonElement value)
    {
        var values = value
            .EnumerateArray()
            .Take(20)
            .Select(ReadSafeValue)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    private static string? Truncate(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maximumLength
                ? value
                : value[..maximumLength];

    private sealed record AuditRow(
        Guid Id,
        string Action,
        string? EntityType,
        string? EntityId,
        string Result,
        DateTimeOffset OccurredAt,
        Guid? CorrelationId,
        string? Metadata,
        string? UserAgent,
        Guid? ActorId,
        string? ActorFirstName,
        string? ActorLastName);
}
