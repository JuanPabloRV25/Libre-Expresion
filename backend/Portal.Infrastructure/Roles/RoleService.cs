using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Portal.Application.Identity;
using Portal.Application.Roles;
using Portal.Domain.Auditing;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Roles;

public sealed class RoleService(
    ApplicationDbContext dbContext,
    RoleManager<ApplicationRole> roleManager,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider) : IRoleService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Roles.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(role =>
                (role.Name != null && role.Name.ToLower().Contains(term))
                || (role.Description != null
                    && role.Description.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(role => role.IsActive == isActive.Value);
        }

        var roles = await query
            .OrderBy(role => role.Name)
            .ToArrayAsync(cancellationToken);
        return await BuildDtosAsync(roles, cancellationToken);
    }

    public async Task<RoleDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var role = await dbContext.Roles
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (role is null)
        {
            return null;
        }

        return (await BuildDtosAsync([role], cancellationToken)).Single();
    }

    public async Task<RoleOperationResult> CreateAsync(
        CreateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(command.Name, command.Description);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        if (await roleManager.FindByNameAsync(validation.Name!) is not null)
        {
            return DuplicateName();
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var role = new ApplicationRole
        {
            Id = Guid.NewGuid(),
            Name = validation.Name,
            Description = validation.Description,
            IsActive = true,
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var identityResult = await roleManager.CreateAsync(role);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        await AddAuditAsync(
            "role.created",
            role.Id,
            new { role.Name },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new RoleOperationResult(
            RoleOperationStatus.Success,
            (await BuildDtosAsync([role], cancellationToken)).Single());
    }

    public async Task<RoleOperationResult> UpdateAsync(
        Guid id,
        UpdateRoleCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = Validate(command.Name, command.Description);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem
            && !string.Equals(role.Name, validation.Name, StringComparison.Ordinal))
        {
            return Protected("Los roles de sistema no pueden renombrarse.");
        }

        var sameNameRole = await roleManager.FindByNameAsync(validation.Name!);
        if (sameNameRole is not null && sameNameRole.Id != role.Id)
        {
            return DuplicateName();
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var previousName = role.Name;
        role.Name = validation.Name;
        role.Description = validation.Description;
        role.UpdatedAt = timeProvider.GetUtcNow();

        var identityResult = await roleManager.UpdateAsync(role);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        await AddAuditAsync(
            "role.updated",
            role.Id,
            new { PreviousName = previousName, role.Name },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new RoleOperationResult(
            RoleOperationStatus.Success,
            (await BuildDtosAsync([role], cancellationToken)).Single());
    }

    public async Task<RoleOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var role = await roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return NotFound();
        }

        if (role.IsSystem && !isActive)
        {
            return Protected("Los roles de sistema no pueden inactivarse.");
        }

        if (role.IsActive == isActive)
        {
            return new RoleOperationResult(
                RoleOperationStatus.Success,
                (await BuildDtosAsync([role], cancellationToken)).Single());
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        role.IsActive = isActive;
        role.UpdatedAt = timeProvider.GetUtcNow();
        var identityResult = await roleManager.UpdateAsync(role);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        await AddAuditAsync(
            isActive ? "role.activated" : "role.deactivated",
            role.Id,
            new { IsActive = isActive },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new RoleOperationResult(
            RoleOperationStatus.Success,
            (await BuildDtosAsync([role], cancellationToken)).Single());
    }

    public async Task<RoleOperationResult> UpdatePermissionsAsync(
        Guid id,
        IReadOnlyList<string> permissionCodes,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var role = await dbContext.Roles
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (role is null)
        {
            return NotFound();
        }

        if (permissionCodes.Any(string.IsNullOrWhiteSpace))
        {
            return InvalidPermissions("Los códigos de permiso no pueden estar vacíos.");
        }

        var requestedCodes = permissionCodes
            .Select(code => code.Trim())
            .ToArray();
        if (requestedCodes.Distinct(StringComparer.Ordinal).Count() != requestedCodes.Length)
        {
            return InvalidPermissions("La lista no puede contener permisos duplicados.");
        }

        if (role.IsSystem
            && (!requestedCodes.ToHashSet(StringComparer.Ordinal)
                .SetEquals(PermissionCodes.All)))
        {
            return Protected(
                "El rol Superadmin debe conservar los 17 permisos oficiales.");
        }

        var permissions = await dbContext.Permissions
            .Where(permission => requestedCodes.Contains(permission.Code)
                && permission.IsActive)
            .ToArrayAsync(cancellationToken);
        if (permissions.Length != requestedCodes.Length)
        {
            return InvalidPermissions(
                "Uno o más permisos no existen o no están activos.");
        }

        var currentAssignments = await (
                from assignment in dbContext.RolePermissions
                join permission in dbContext.Permissions
                    on assignment.PermissionId equals permission.Id
                where assignment.RoleId == role.Id
                select new { Assignment = assignment, permission.Code })
            .ToArrayAsync(cancellationToken);
        var requestedCodeSet = requestedCodes.ToHashSet(StringComparer.Ordinal);
        var currentCodeSet = currentAssignments
            .Select(item => item.Code)
            .ToHashSet(StringComparer.Ordinal);
        var addedCodes = requestedCodeSet
            .Except(currentCodeSet, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var removedCodes = currentCodeSet
            .Except(requestedCodeSet, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (addedCodes.Length == 0 && removedCodes.Length == 0)
        {
            return new RoleOperationResult(
                RoleOperationStatus.Success,
                (await BuildDtosAsync([role], cancellationToken)).Single());
        }

        var currentUser = await currentUserService.GetCurrentAsync(cancellationToken);
        if (currentUser is null)
        {
            return PermissionDelegationForbidden();
        }

        var actorPermissionCodes = await (
                from userRole in dbContext.Set<ApplicationUserRole>().AsNoTracking()
                join actorRole in dbContext.Roles.AsNoTracking()
                    on userRole.RoleId equals actorRole.Id
                join rolePermission in dbContext.RolePermissions.AsNoTracking()
                    on actorRole.Id equals rolePermission.RoleId
                join permission in dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where userRole.UserId == currentUser.Id
                    && actorRole.IsActive
                    && permission.IsActive
                select permission.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var actorPermissionSet = actorPermissionCodes.ToHashSet(StringComparer.Ordinal);
        if (addedCodes.Any(code => !actorPermissionSet.Contains(code)))
        {
            return PermissionDelegationForbidden();
        }

        var now = timeProvider.GetUtcNow();
        var permissionsByCode = permissions.ToDictionary(
            permission => permission.Code,
            StringComparer.Ordinal);

        foreach (var code in addedCodes)
        {
            dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permissionsByCode[code].Id,
                AssignedAt = now,
                AssignedByUserId = currentUser?.Id,
            });
        }

        dbContext.RolePermissions.RemoveRange(
            currentAssignments
                .Where(item => removedCodes.Contains(item.Code, StringComparer.Ordinal))
                .Select(item => item.Assignment));
        role.UpdatedAt = now;
        await AddAuditAsync(
            "role.permissions_updated",
            role.Id,
            new { AddedCodes = addedCodes, RemovedCodes = removedCodes },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new RoleOperationResult(
            RoleOperationStatus.Success,
            (await BuildDtosAsync([role], cancellationToken)).Single());
    }

    private async Task<IReadOnlyList<RoleDto>> BuildDtosAsync(
        IReadOnlyCollection<ApplicationRole> roles,
        CancellationToken cancellationToken)
    {
        if (roles.Count == 0)
        {
            return [];
        }

        var roleIds = roles.Select(role => role.Id).ToArray();
        var permissions = await (
                from assignment in dbContext.RolePermissions.AsNoTracking()
                join permission in dbContext.Permissions.AsNoTracking()
                    on assignment.PermissionId equals permission.Id
                where roleIds.Contains(assignment.RoleId)
                orderby permission.Code
                select new { assignment.RoleId, permission.Code })
            .ToArrayAsync(cancellationToken);
        var userCounts = await dbContext.Set<ApplicationUserRole>()
            .AsNoTracking()
            .Where(assignment => roleIds.Contains(assignment.RoleId))
            .GroupBy(assignment => assignment.RoleId)
            .Select(group => new { RoleId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.RoleId, item => item.Count, cancellationToken);
        var permissionLookup = permissions.ToLookup(
            item => item.RoleId,
            item => item.Code);

        return roles.Select(role => new RoleDto(
                role.Id,
                role.Name ?? string.Empty,
                role.Description,
                role.IsActive,
                role.IsSystem,
                userCounts.GetValueOrDefault(role.Id),
                permissionLookup[role.Id].ToArray(),
                role.CreatedAt,
                role.UpdatedAt))
            .ToArray();
    }

    private async Task AddAuditAsync(
        string action,
        Guid roleId,
        object metadata,
        CancellationToken cancellationToken)
    {
        var currentUser = await currentUserService.GetCurrentAsync(cancellationToken);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = currentUser?.Id,
            Action = action,
            EntityType = "Role",
            EntityId = roleId.ToString(),
            Result = "Success",
            OccurredAt = timeProvider.GetUtcNow(),
            Metadata = JsonSerializer.Serialize(metadata),
        });
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        CancellationToken cancellationToken) => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) => transaction is null
            ? Task.CompletedTask
            : transaction.CommitAsync(cancellationToken);

    private static (string? Name, string? Description, RoleOperationResult? Error)
        Validate(string? name, string? description)
    {
        var normalizedName = name?.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return (null, null, Invalid("El nombre del rol es obligatorio."));
        }

        if (normalizedName.Length > 256)
        {
            return (null, null, Invalid("El nombre del rol no puede superar 256 caracteres."));
        }

        if (normalizedDescription?.Length > 500)
        {
            return (null, null, Invalid("La descripción del rol no puede superar 500 caracteres."));
        }

        return (normalizedName, normalizedDescription, null);
    }

    private static RoleOperationResult MapIdentityFailure(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code == "DuplicateRoleName"))
        {
            return DuplicateName();
        }

        if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
        {
            return new RoleOperationResult(
                RoleOperationStatus.Conflict,
                ErrorCode: "role_concurrency_conflict",
                ErrorMessage: "El rol fue modificado por otra operación. Recarga e intenta nuevamente.");
        }

        return Invalid("Los datos del rol no son válidos.");
    }

    private static RoleOperationResult Invalid(string message) => new(
        RoleOperationStatus.Invalid,
        ErrorCode: "validation_error",
        ErrorMessage: message);

    private static RoleOperationResult InvalidPermissions(string message) => new(
        RoleOperationStatus.Invalid,
        ErrorCode: "invalid_permission_codes",
        ErrorMessage: message);

    private static RoleOperationResult DuplicateName() => new(
        RoleOperationStatus.Duplicate,
        ErrorCode: "role_name_conflict",
        ErrorMessage: "Ya existe un rol con ese nombre.");

    private static RoleOperationResult NotFound() => new(
        RoleOperationStatus.NotFound,
        ErrorCode: "role_not_found",
        ErrorMessage: "El rol solicitado no existe.");

    private static RoleOperationResult Protected(string message) => new(
        RoleOperationStatus.Protected,
        ErrorCode: "system_role_protected",
        ErrorMessage: message);

    private static RoleOperationResult PermissionDelegationForbidden() => new(
        RoleOperationStatus.Protected,
        ErrorCode: "permission_delegation_forbidden",
        ErrorMessage: "No puedes delegar permisos que no posees.");
}
