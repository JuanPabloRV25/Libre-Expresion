using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Portal.Application.Areas;
using Portal.Application.Identity;
using Portal.Domain.Areas;
using Portal.Domain.Auditing;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Areas;

public sealed class AreaService(
    ApplicationDbContext dbContext,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider) : IAreaService
{
    public async Task<IReadOnlyList<AreaDto>> ListAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Areas.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(area =>
                area.Name.ToLower().Contains(term)
                || (area.Description != null
                    && area.Description.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(area => area.IsActive == isActive.Value);
        }

        return await query
            .OrderBy(area => area.Name)
            .Select(area => ToDto(area))
            .ToArrayAsync(cancellationToken);
    }

    public Task<AreaDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        dbContext.Areas
            .AsNoTracking()
            .Where(area => area.Id == id)
            .Select(area => ToDto(area))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<AreaOperationResult> CreateAsync(
        CreateAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(command.Name, command.Description);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        if (await NameExistsAsync(validation.Name!, null, cancellationToken))
        {
            return DuplicateName();
        }

        var now = timeProvider.GetUtcNow();
        var area = new Area
        {
            Id = Guid.NewGuid(),
            Name = validation.Name!,
            Description = validation.Description,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Areas.Add(area);
        await AddAuditAsync(
            "area.created",
            area.Id,
            new { area.Name },
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateName();
        }

        return new AreaOperationResult(
            AreaOperationStatus.Success,
            ToDto(area));
    }

    public async Task<AreaOperationResult> UpdateAsync(
        Guid id,
        UpdateAreaCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(command.Name, command.Description);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var area = await dbContext.Areas
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (area is null)
        {
            return NotFound();
        }

        if (await NameExistsAsync(validation.Name!, id, cancellationToken))
        {
            return DuplicateName();
        }

        var previousName = area.Name;
        area.Name = validation.Name!;
        area.Description = validation.Description;
        area.UpdatedAt = timeProvider.GetUtcNow();

        await AddAuditAsync(
            "area.updated",
            area.Id,
            new { PreviousName = previousName, area.Name },
            cancellationToken);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return DuplicateName();
        }

        return new AreaOperationResult(
            AreaOperationStatus.Success,
            ToDto(area));
    }

    public async Task<AreaOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var area = await dbContext.Areas
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (area is null)
        {
            return NotFound();
        }

        if (area.IsActive == isActive)
        {
            return new AreaOperationResult(
                AreaOperationStatus.Success,
                ToDto(area));
        }

        area.IsActive = isActive;
        area.UpdatedAt = timeProvider.GetUtcNow();
        await AddAuditAsync(
            isActive ? "area.activated" : "area.deactivated",
            area.Id,
            new { IsActive = isActive },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AreaOperationResult(
            AreaOperationStatus.Success,
            ToDto(area));
    }

    private async Task<bool> NameExistsAsync(
        string name,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var normalizedName = name.ToUpper();
        return await dbContext.Areas.AnyAsync(
            area => area.Name.ToUpper() == normalizedName
                && (!excludedId.HasValue || area.Id != excludedId.Value),
            cancellationToken);
    }

    private async Task AddAuditAsync(
        string action,
        Guid entityId,
        object metadata,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentAsync(cancellationToken);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser?.Id,
            Action = action,
            EntityType = "Area",
            EntityId = entityId.ToString(),
            Result = "Success",
            OccurredAt = timeProvider.GetUtcNow(),
            Metadata = JsonSerializer.Serialize(metadata),
        });
    }

    private static (string? Name, string? Description, AreaOperationResult? Error)
        Validate(string? name, string? description)
    {
        var normalizedName = name?.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return (null, null, Invalid("El nombre del área es obligatorio."));
        }

        if (normalizedName.Length > 120)
        {
            return (null, null, Invalid("El nombre del área no puede superar 120 caracteres."));
        }

        if (normalizedDescription?.Length > 500)
        {
            return (null, null, Invalid("La descripción del área no puede superar 500 caracteres."));
        }

        return (normalizedName, normalizedDescription, null);
    }

    private static AreaDto ToDto(Area area) => new(
        area.Id,
        area.Name,
        area.Description,
        area.IsActive,
        area.CreatedAt,
        area.UpdatedAt);

    private static AreaOperationResult Invalid(string message) => new(
        AreaOperationStatus.Invalid,
        ErrorCode: "validation_error",
        ErrorMessage: message);

    private static AreaOperationResult DuplicateName() => new(
        AreaOperationStatus.Duplicate,
        ErrorCode: "area_name_conflict",
        ErrorMessage: "Ya existe un área con ese nombre.");

    private static AreaOperationResult NotFound() => new(
        AreaOperationStatus.NotFound,
        ErrorCode: "area_not_found",
        ErrorMessage: "El área solicitada no existe.");

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
        };
}
