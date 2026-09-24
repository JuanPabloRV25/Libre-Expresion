using System.Data;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Portal.Application.Identity;
using Portal.Application.Notifications;
using Portal.Application.Users;
using Portal.Domain.Auditing;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;
using Portal.Infrastructure.Persistence.Seeding;

namespace Portal.Infrastructure.Users;

public sealed class UserService(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUserService,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IConfiguration configuration) : IUserService
{
    public async Task<IReadOnlyList<UserDto>> ListAsync(
        string? search,
        bool? isActive,
        Guid? areaId,
        Guid? roleId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(user =>
                (user.UserName != null && user.UserName.ToLower().Contains(term))
                || user.FirstName.ToLower().Contains(term)
                || user.LastName.ToLower().Contains(term)
                || (user.Email != null && user.Email.ToLower().Contains(term)));
        }

        if (isActive.HasValue)
        {
            query = query.Where(user => user.IsActive == isActive.Value);
        }

        if (areaId.HasValue)
        {
            query = query.Where(user => user.AreaId == areaId.Value);
        }

        if (roleId.HasValue)
        {
            query = query.Where(user => dbContext.Set<ApplicationUserRole>().Any(
                assignment => assignment.UserId == user.Id
                    && assignment.RoleId == roleId.Value));
        }

        var users = await query
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .ToArrayAsync(cancellationToken);
        return await BuildDtosAsync(users, cancellationToken);
    }

    public async Task<UserDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return (await BuildDtosAsync([user], cancellationToken)).Single();
    }

    public async Task<UserOperationResult> CreateAsync(
        CreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreate(command);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        if (await userManager.FindByNameAsync(validation.DocumentNumber!) is not null)
        {
            return DuplicateDocument();
        }

        if (await userManager.FindByEmailAsync(validation.Email!) is not null)
        {
            return DuplicateEmail();
        }

        var areaValidation = await ValidateAreaAsync(
            command.AreaId,
            requireActive: true,
            cancellationToken);
        if (areaValidation is not null)
        {
            return areaValidation;
        }

        await using var transaction = await BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var roleValidation = await ValidateRolesAsync(
            command.RoleIds!,
            requireAllActive: true,
            cancellationToken);
        if (roleValidation.Error is not null)
        {
            return roleValidation.Error;
        }

        if (roleValidation.Roles!.Length > 0)
        {
            var delegationError = await ValidateRoleDelegationAsync(
                roleValidation.Roles,
                requireAssignmentPermission: true,
                cancellationToken);
            if (delegationError is not null)
            {
                return delegationError;
            }
        }

        var now = timeProvider.GetUtcNow();
        var actor = await currentUserService.GetCurrentAsync(cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = validation.DocumentNumber,
            FirstName = validation.FirstName!,
            LastName = validation.LastName!,
            Email = validation.Email,
            AreaId = command.AreaId,
            AdvisorCode = validation.AdvisorCode,
            IsActive = command.IsActive,
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        // La credencial predecible se habilita exclusivamente mediante
        // configuración de DEV. Se asigna con el hasher de Identity sin relajar
        // la política general de contraseñas del Portal.
        if (UseDocumentAsTemporaryPassword())
        {
            user.PasswordHash = userManager.PasswordHasher.HashPassword(
                user,
                validation.DocumentNumber!);
        }

        var identityResult = await userManager.CreateAsync(user);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        foreach (var role in roleValidation.Roles!)
        {
            dbContext.Set<ApplicationUserRole>().Add(new ApplicationUserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                AssignedAt = now,
                AssignedByUserId = actor?.Id,
            });
        }

        await AddAuditAsync(
            "user.created",
            user.Id,
            new
            {
                RoleIds = roleValidation.Roles!.Select(role => role.Id),
                user.AreaId,
            },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        var encodedPasswordToken = PasswordResetTokenCodec.Encode(
            await userManager.GeneratePasswordResetTokenAsync(user));
        var notification = await notificationService.NotifyUserCreatedAsync(
            ToNotificationRecipient(user),
            encodedPasswordToken,
            CancellationToken.None);
        await AddNotificationAuditAsync(
            user.Id,
            "user_created",
            notification,
            NotificationAuditActions.UserCreatedSent,
            NotificationAuditActions.UserCreatedFailed);

        return new UserOperationResult(
            UserOperationStatus.Success,
            (await BuildDtosAsync([user], CancellationToken.None)).Single(),
            NotificationStatus: notification.Status);
    }

    public async Task<UserOperationResult> UpdateAsync(
        Guid id,
        UpdateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateUpdate(command);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        var existingEmail = await userManager.FindByEmailAsync(validation.Email!);
        if (existingEmail is not null && existingEmail.Id != user.Id)
        {
            return DuplicateEmail();
        }

        var areaValidation = await ValidateAreaAsync(
            command.AreaId,
            requireActive: command.AreaId != user.AreaId,
            cancellationToken);
        if (areaValidation is not null)
        {
            return areaValidation;
        }

        await using var transaction = await BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        var previousEmail = user.Email;
        var emailChanged = !string.Equals(
            previousEmail,
            validation.Email,
            StringComparison.OrdinalIgnoreCase);
        user.FirstName = validation.FirstName!;
        user.LastName = validation.LastName!;
        user.Email = validation.Email;
        user.AreaId = command.AreaId;
        user.AdvisorCode = validation.AdvisorCode;
        user.UpdatedAt = timeProvider.GetUtcNow();

        var identityResult = await userManager.UpdateAsync(user);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        if (emailChanged)
        {
            identityResult = await userManager.UpdateSecurityStampAsync(user);
            if (!identityResult.Succeeded)
            {
                return MapIdentityFailure(identityResult);
            }
        }

        await AddAuditAsync(
            "user.updated",
            user.Id,
            new { PreviousEmail = previousEmail, user.Email, user.AreaId },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new UserOperationResult(
            UserOperationStatus.Success,
            (await BuildDtosAsync([user], cancellationToken)).Single());
    }

    public async Task<UserOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (user.IsActive == isActive)
        {
            return new UserOperationResult(
                UserOperationStatus.Success,
                (await BuildDtosAsync([user], cancellationToken)).Single());
        }

        await using var transaction = await BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (!isActive
            && await WouldRemoveLastActiveSuperadminAsync(user, cancellationToken))
        {
            return LastSuperadmin();
        }

        user.IsActive = isActive;
        user.UpdatedAt = timeProvider.GetUtcNow();
        var identityResult = isActive
            ? await userManager.UpdateAsync(user)
            : await userManager.UpdateSecurityStampAsync(user);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        await AddAuditAsync(
            isActive ? "user.activated" : "user.deactivated",
            user.Id,
            new { IsActive = isActive },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new UserOperationResult(
            UserOperationStatus.Success,
            (await BuildDtosAsync([user], cancellationToken)).Single());
    }

    public async Task<UserOperationResult> UpdateRolesAsync(
        Guid id,
        IReadOnlyList<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        if (roleIds.Distinct().Count() != roleIds.Count)
        {
            return InvalidRoles("La lista no puede contener roles duplicados.");
        }

        await using var transaction = await BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var user = await dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var roles = await dbContext.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToArrayAsync(cancellationToken);
        if (roles.Length != roleIds.Count)
        {
            return InvalidRoles("Uno o más roles no existen.");
        }

        var currentAssignments = await dbContext.Set<ApplicationUserRole>()
            .Where(assignment => assignment.UserId == user.Id)
            .ToArrayAsync(cancellationToken);
        var currentRoleIds = currentAssignments
            .Select(assignment => assignment.RoleId)
            .ToHashSet();
        var requestedRoleIds = roleIds.ToHashSet();
        var addedRoleIds = requestedRoleIds.Except(currentRoleIds).ToArray();
        var removedRoleIds = currentRoleIds.Except(requestedRoleIds).ToArray();

        if (roles.Any(role => addedRoleIds.Contains(role.Id) && !role.IsActive))
        {
            return InvalidRoles("No se pueden asignar roles inactivos.");
        }

        var delegationError = await ValidateRoleDelegationAsync(
            roles.Where(role => addedRoleIds.Contains(role.Id)).ToArray(),
            requireAssignmentPermission: true,
            cancellationToken);
        if (delegationError is not null)
        {
            return delegationError;
        }

        var superadminRoleId = await GetSuperadminRoleIdAsync(cancellationToken);
        if (user.IsActive
            && superadminRoleId.HasValue
            && removedRoleIds.Contains(superadminRoleId.Value)
            && await CountActiveSuperadminsAsync(
                superadminRoleId.Value,
                cancellationToken) <= 1)
        {
            return LastSuperadmin();
        }

        if (addedRoleIds.Length == 0 && removedRoleIds.Length == 0)
        {
            return new UserOperationResult(
                UserOperationStatus.Success,
                (await BuildDtosAsync([user], cancellationToken)).Single());
        }

        var actor = await currentUserService.GetCurrentAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        foreach (var roleId in addedRoleIds)
        {
            dbContext.Set<ApplicationUserRole>().Add(new ApplicationUserRole
            {
                UserId = user.Id,
                RoleId = roleId,
                AssignedAt = now,
                AssignedByUserId = actor?.Id,
            });
        }

        dbContext.Set<ApplicationUserRole>().RemoveRange(
            currentAssignments.Where(
                assignment => removedRoleIds.Contains(assignment.RoleId)));
        user.UpdatedAt = now;
        await AddAuditAsync(
            "user.roles_updated",
            user.Id,
            new { AddedRoleIds = addedRoleIds, RemovedRoleIds = removedRoleIds },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        return new UserOperationResult(
            UserOperationStatus.Success,
            (await BuildDtosAsync([user], cancellationToken)).Single());
    }

    public async Task<UserOperationResult> ResetPasswordAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        await using var transaction = await BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        if (await WouldRemoveLastActiveSuperadminAsync(user, cancellationToken))
        {
            return new UserOperationResult(
                UserOperationStatus.Conflict,
                ErrorCode: "last_superadmin_password_reset_forbidden",
                ErrorMessage: "No se puede restablecer la contraseña del único Superadmin activo. Utiliza el cambio de contraseña del perfil.");
        }

        var identityResult = await userManager.HasPasswordAsync(user)
            ? await userManager.RemovePasswordAsync(user)
            : await userManager.UpdateSecurityStampAsync(user);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        if (UseDocumentAsTemporaryPassword())
        {
            user.PasswordHash = userManager.PasswordHasher.HashPassword(
                user,
                user.UserName
                    ?? throw new InvalidOperationException("The user does not have a document number."));
            identityResult = await userManager.UpdateAsync(user);
            if (!identityResult.Succeeded)
            {
                return MapIdentityFailure(identityResult);
            }
        }

        user.MustChangePassword = true;
        user.UpdatedAt = timeProvider.GetUtcNow();
        identityResult = await userManager.UpdateAsync(user);
        if (!identityResult.Succeeded)
        {
            return MapIdentityFailure(identityResult);
        }

        await AddAuditAsync(
            "password_reset.requested",
            user.Id,
            new { },
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await CommitAsync(transaction, cancellationToken);

        var encodedPasswordToken = PasswordResetTokenCodec.Encode(
            await userManager.GeneratePasswordResetTokenAsync(user));
        var notification = await notificationService.NotifyPasswordResetAsync(
            ToNotificationRecipient(user),
            encodedPasswordToken,
            CancellationToken.None);
        await AddNotificationAuditAsync(
            user.Id,
            "password_reset",
            notification,
            NotificationAuditActions.PasswordResetSent,
            NotificationAuditActions.PasswordResetFailed);

        return new UserOperationResult(
            UserOperationStatus.Success,
            (await BuildDtosAsync([user], CancellationToken.None)).Single(),
            NotificationStatus: notification.Status);
    }

    private async Task<IReadOnlyList<UserDto>> BuildDtosAsync(
        IReadOnlyCollection<ApplicationUser> users,
        CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            return [];
        }

        var areaIds = users
            .Where(user => user.AreaId.HasValue)
            .Select(user => user.AreaId!.Value)
            .Distinct()
            .ToArray();
        var areas = await dbContext.Areas
            .AsNoTracking()
            .Where(area => areaIds.Contains(area.Id))
            .ToDictionaryAsync(area => area.Id, cancellationToken);
        var userIds = users.Select(user => user.Id).ToArray();
        var roleRows = await (
                from assignment in dbContext.Set<ApplicationUserRole>().AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on assignment.RoleId equals role.Id
                where userIds.Contains(assignment.UserId)
                orderby role.Name
                select new
                {
                    assignment.UserId,
                    Role = new UserRoleDto(
                        role.Id,
                        role.Name ?? string.Empty,
                        role.IsActive,
                        role.IsSystem),
                })
            .ToArrayAsync(cancellationToken);
        var roleLookup = roleRows.ToLookup(row => row.UserId, row => row.Role);

        return users.Select(user => new UserDto(
                user.Id,
                user.UserName ?? string.Empty,
                user.FirstName,
                user.LastName,
                user.Email ?? string.Empty,
                user.AreaId.HasValue
                    && areas.TryGetValue(user.AreaId.Value, out var area)
                        ? new UserAreaDto(area.Id, area.Name, area.IsActive)
                        : null,
                user.AdvisorCode,
                roleLookup[user.Id].ToArray(),
                user.IsActive,
                user.MustChangePassword,
                user.CreatedAt,
                user.UpdatedAt))
            .ToArray();
    }

    private async Task<UserOperationResult?> ValidateAreaAsync(
        Guid? areaId,
        bool requireActive,
        CancellationToken cancellationToken)
    {
        if (!areaId.HasValue)
        {
            return null;
        }

        var area = await dbContext.Areas
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == areaId.Value, cancellationToken);
        if (area is null)
        {
            return new UserOperationResult(
                UserOperationStatus.Invalid,
                ErrorCode: "area_not_found",
                ErrorMessage: "El área seleccionada no existe.");
        }

        return requireActive && !area.IsActive
            ? new UserOperationResult(
                UserOperationStatus.Invalid,
                ErrorCode: "inactive_area",
                ErrorMessage: "No se puede realizar una nueva asignación a un área inactiva.")
            : null;
    }

    private async Task<(ApplicationRole[]? Roles, UserOperationResult? Error)>
        ValidateRolesAsync(
            IReadOnlyList<Guid> roleIds,
            bool requireAllActive,
            CancellationToken cancellationToken)
    {
        if (roleIds.Distinct().Count() != roleIds.Count)
        {
            return (null, InvalidRoles("La lista no puede contener roles duplicados."));
        }

        var roles = await dbContext.Roles
            .Where(role => roleIds.Contains(role.Id))
            .ToArrayAsync(cancellationToken);
        if (roles.Length != roleIds.Count)
        {
            return (null, InvalidRoles("Uno o más roles no existen."));
        }

        if (requireAllActive && roles.Any(role => !role.IsActive))
        {
            return (null, InvalidRoles("No se pueden asignar roles inactivos."));
        }

        return (roles, null);
    }

    private async Task<UserOperationResult?> ValidateRoleDelegationAsync(
        IReadOnlyCollection<ApplicationRole> roles,
        bool requireAssignmentPermission,
        CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetCurrentAsync(cancellationToken);
        if (actor is null)
        {
            return RoleAssignmentForbidden();
        }

        var actorRoles = await (
                from assignment in dbContext.Set<ApplicationUserRole>().AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on assignment.RoleId equals role.Id
                where assignment.UserId == actor.Id && role.IsActive
                select role)
            .ToArrayAsync(cancellationToken);
        var actorIsSuperadmin = actorRoles.Any(role =>
            role.IsSystem
            && role.Name == DatabaseSeeder.SuperadminRoleName);
        if (roles.Any(role => role.IsSystem) && !actorIsSuperadmin)
        {
            return RoleAssignmentForbidden();
        }

        var actorPermissionCodes = await (
                from assignment in dbContext.Set<ApplicationUserRole>().AsNoTracking()
                join role in dbContext.Roles.AsNoTracking()
                    on assignment.RoleId equals role.Id
                join rolePermission in dbContext.RolePermissions.AsNoTracking()
                    on role.Id equals rolePermission.RoleId
                join permission in dbContext.Permissions.AsNoTracking()
                    on rolePermission.PermissionId equals permission.Id
                where assignment.UserId == actor.Id
                    && role.IsActive
                    && permission.IsActive
                select permission.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var actorPermissionSet = actorPermissionCodes.ToHashSet(StringComparer.Ordinal);
        if (requireAssignmentPermission
            && !actorPermissionSet.Contains(PermissionCodes.UsersAssignRoles))
        {
            return RoleAssignmentForbidden();
        }

        var delegatedRoleIds = roles.Select(role => role.Id).ToArray();
        var delegatedPermissionCodes = await (
                from assignment in dbContext.RolePermissions.AsNoTracking()
                join permission in dbContext.Permissions.AsNoTracking()
                    on assignment.PermissionId equals permission.Id
                where delegatedRoleIds.Contains(assignment.RoleId)
                    && permission.IsActive
                select permission.Code)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return delegatedPermissionCodes.Any(code => !actorPermissionSet.Contains(code))
            ? RoleAssignmentForbidden()
            : null;
    }

    private async Task<bool> WouldRemoveLastActiveSuperadminAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (!user.IsActive)
        {
            return false;
        }

        var roleId = await GetSuperadminRoleIdAsync(cancellationToken);
        if (!roleId.HasValue)
        {
            return false;
        }

        var hasRole = await dbContext.Set<ApplicationUserRole>().AnyAsync(
            assignment => assignment.UserId == user.Id
                && assignment.RoleId == roleId.Value,
            cancellationToken);
        return hasRole
            && await CountActiveSuperadminsAsync(roleId.Value, cancellationToken) <= 1;
    }

    private Task<Guid?> GetSuperadminRoleIdAsync(
        CancellationToken cancellationToken) => dbContext.Roles
        .Where(role => role.IsSystem
            && role.Name == DatabaseSeeder.SuperadminRoleName)
        .Select(role => (Guid?)role.Id)
        .SingleOrDefaultAsync(cancellationToken);

    private Task<int> CountActiveSuperadminsAsync(
        Guid roleId,
        CancellationToken cancellationToken) => (
            from assignment in dbContext.Set<ApplicationUserRole>()
            join user in dbContext.Users on assignment.UserId equals user.Id
            where assignment.RoleId == roleId && user.IsActive
            select user.Id)
        .Distinct()
        .CountAsync(cancellationToken);

    private async Task AddAuditAsync(
        string action,
        Guid userId,
        object metadata,
        CancellationToken cancellationToken)
    {
        var actor = await currentUserService.GetCurrentAsync(cancellationToken);
        dbContext.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = actor?.Id,
            Action = action,
            EntityType = "User",
            EntityId = userId.ToString(),
            Result = "Success",
            OccurredAt = timeProvider.GetUtcNow(),
            Metadata = JsonSerializer.Serialize(metadata),
        });
    }

    private async Task AddNotificationAuditAsync(
        Guid userId,
        string notificationType,
        NotificationResult notification,
        string sentAction,
        string failedAction)
    {
        var action = notification.Status == NotificationStatuses.Sent
            ? sentAction
            : failedAction;
        await AddAuditAsync(
            action,
            userId,
            new
            {
                NotificationType = notificationType,
                DeliveryResult = notification.Status,
                TargetUserId = userId,
            },
            CancellationToken.None);
        var auditEvent = dbContext.ChangeTracker.Entries<AuditEvent>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .Last(audit => audit.Action == action && audit.EntityId == userId.ToString());
        auditEvent.Result = notification.Status == NotificationStatuses.Sent
            ? "Success"
            : "Failed";
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private static NotificationRecipient ToNotificationRecipient(
        ApplicationUser user) => new(
            user.Id,
            user.Email ?? throw new InvalidOperationException("The user does not have an email address."),
            user.FirstName,
            user.LastName,
            user.UserName ?? throw new InvalidOperationException("The user does not have a document number."));

    private async Task<IDbContextTransaction?> BeginTransactionAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken) => dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken)
            : null;

    private static Task CommitAsync(
        IDbContextTransaction? transaction,
        CancellationToken cancellationToken) => transaction is null
            ? Task.CompletedTask
            : transaction.CommitAsync(cancellationToken);

    private bool UseDocumentAsTemporaryPassword() => string.Equals(
        configuration["Authentication:UseDocumentAsTemporaryPassword"],
        "true",
        StringComparison.OrdinalIgnoreCase);

    private static (
        string? DocumentNumber,
        string? FirstName,
        string? LastName,
        string? Email,
        string? AdvisorCode,
        UserOperationResult? Error) ValidateCreate(CreateUserCommand command)
    {
        var common = ValidateCommon(
            command.FirstName,
            command.LastName,
            command.Email,
            command.AdvisorCode);
        if (common.Error is not null)
        {
            return (null, null, null, null, null, common.Error);
        }

        var document = command.DocumentNumber?.Trim();
        if (string.IsNullOrWhiteSpace(document))
        {
            return (null, null, null, null, null, Invalid("El documento es obligatorio."));
        }

        if (document.Length > 256)
        {
            return (null, null, null, null, null, Invalid("El documento no puede superar 256 caracteres."));
        }

        if (command.RoleIds is null)
        {
            return (null, null, null, null, null, InvalidRoles("La lista de roles es obligatoria."));
        }

        return (
            document,
            common.FirstName,
            common.LastName,
            common.Email,
            common.AdvisorCode,
            null);
    }

    private static (
        string? FirstName,
        string? LastName,
        string? Email,
        string? AdvisorCode,
        UserOperationResult? Error) ValidateUpdate(UpdateUserCommand command) =>
        ValidateCommon(
            command.FirstName,
            command.LastName,
            command.Email,
            command.AdvisorCode);

    private static (
        string? FirstName,
        string? LastName,
        string? Email,
        string? AdvisorCode,
        UserOperationResult? Error) ValidateCommon(
            string? firstName,
            string? lastName,
            string? email,
            string? advisorCode)
    {
        var normalizedFirstName = firstName?.Trim();
        var normalizedLastName = lastName?.Trim();
        var normalizedEmail = email?.Trim();
        var normalizedAdvisorCode = string.IsNullOrWhiteSpace(advisorCode)
            ? null
            : advisorCode.Trim();

        if (string.IsNullOrWhiteSpace(normalizedFirstName))
        {
            return (null, null, null, null, Invalid("El nombre es obligatorio."));
        }

        if (normalizedFirstName.Length > 120)
        {
            return (null, null, null, null, Invalid("El nombre no puede superar 120 caracteres."));
        }

        if (string.IsNullOrWhiteSpace(normalizedLastName))
        {
            return (null, null, null, null, Invalid("El apellido es obligatorio."));
        }

        if (normalizedLastName.Length > 120)
        {
            return (null, null, null, null, Invalid("El apellido no puede superar 120 caracteres."));
        }

        if (string.IsNullOrWhiteSpace(normalizedEmail)
            || !MailAddress.TryCreate(normalizedEmail, out var parsedEmail)
            || !string.Equals(
                parsedEmail.Address,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, null, null, Invalid("El correo electrónico no es válido."));
        }

        if (normalizedEmail.Length > 256)
        {
            return (null, null, null, null, Invalid("El correo no puede superar 256 caracteres."));
        }

        if (normalizedAdvisorCode?.Length > 100)
        {
            return (null, null, null, null, Invalid("El código de asesor no puede superar 100 caracteres."));
        }

        return (
            normalizedFirstName,
            normalizedLastName,
            normalizedEmail,
            normalizedAdvisorCode,
            null);
    }

    private static UserOperationResult MapIdentityFailure(IdentityResult result)
    {
        if (result.Errors.Any(error => error.Code == "DuplicateUserName"))
        {
            return DuplicateDocument();
        }

        if (result.Errors.Any(error => error.Code == "DuplicateEmail"))
        {
            return DuplicateEmail();
        }

        if (result.Errors.Any(error => error.Code == "ConcurrencyFailure"))
        {
            return new UserOperationResult(
                UserOperationStatus.Conflict,
                ErrorCode: "user_concurrency_conflict",
                ErrorMessage: "El usuario fue modificado por otra operación. Recarga e intenta nuevamente.");
        }

        return Invalid("Los datos del usuario no son válidos.");
    }

    private static UserOperationResult Invalid(string message) => new(
        UserOperationStatus.Invalid,
        ErrorCode: "validation_error",
        ErrorMessage: message);

    private static UserOperationResult InvalidRoles(string message) => new(
        UserOperationStatus.Invalid,
        ErrorCode: "invalid_role_ids",
        ErrorMessage: message);

    private static UserOperationResult DuplicateDocument() => new(
        UserOperationStatus.DuplicateDocument,
        ErrorCode: "document_conflict",
        ErrorMessage: "Ya existe un usuario con ese documento.");

    private static UserOperationResult DuplicateEmail() => new(
        UserOperationStatus.DuplicateEmail,
        ErrorCode: "email_conflict",
        ErrorMessage: "Ya existe un usuario con ese correo electrónico.");

    private static UserOperationResult NotFound() => new(
        UserOperationStatus.NotFound,
        ErrorCode: "user_not_found",
        ErrorMessage: "El usuario solicitado no existe.");

    private static UserOperationResult LastSuperadmin() => new(
        UserOperationStatus.LastSuperadmin,
        ErrorCode: "last_superadmin_required",
        ErrorMessage: "La operación dejaría el sistema sin un Superadmin activo.");

    private static UserOperationResult RoleAssignmentForbidden() => new(
        UserOperationStatus.Conflict,
        ErrorCode: "role_assignment_forbidden",
        ErrorMessage: "No puedes asignar roles con privilegios que no posees.");
}
