using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence.Seeding;

public sealed class DatabaseSeeder(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    TimeProvider timeProvider,
    ILogger<DatabaseSeeder> logger)
{
    public const string SuperadminRoleName = "Superadmin";
    public const string CommercialAgentRoleName = "Agente Comercial";
    public const string CommercialAssistantRoleName = "Auxiliar Comercial";

    private const string SuperadminRoleDescription =
        "Rol de sistema con privilegios globales de administración del Portal Libre Expresión.";

    private static readonly IReadOnlyList<PermissionDefinition> PermissionDefinitions =
    [
        new(PermissionCodes.AreasView, "areas", "view", "Ver áreas"),
        new(PermissionCodes.AreasCreate, "areas", "create", "Crear áreas"),
        new(PermissionCodes.AreasEdit, "areas", "edit", "Editar áreas"),
        new(PermissionCodes.AreasActivate, "areas", "activate", "Activar o inactivar áreas"),
        new(PermissionCodes.UsersView, "users", "view", "Ver usuarios"),
        new(PermissionCodes.UsersCreate, "users", "create", "Crear usuarios"),
        new(PermissionCodes.UsersEdit, "users", "edit", "Editar usuarios"),
        new(PermissionCodes.UsersActivate, "users", "activate", "Activar o inactivar usuarios"),
        new(PermissionCodes.UsersAssignRoles, "users", "assign_roles", "Asignar roles a usuarios"),
        new(PermissionCodes.UsersResetPassword, "users", "reset_password", "Restablecer contraseña de usuarios"),
        new(PermissionCodes.RolesView, "roles", "view", "Ver roles"),
        new(PermissionCodes.RolesCreate, "roles", "create", "Crear roles"),
        new(PermissionCodes.RolesEdit, "roles", "edit", "Editar roles"),
        new(PermissionCodes.RolesActivate, "roles", "activate", "Activar o inactivar roles"),
        new(PermissionCodes.RolesAssignPermissions, "roles", "assign_permissions", "Asignar permisos a roles"),
        new(PermissionCodes.PermissionsView, "permissions", "view", "Ver permisos"),
        new(PermissionCodes.AuditView, "audit", "view", "Ver auditoría"),
        new(CommercialPermissionCodes.OrdersView, "commercial.production_orders", "view", "Ver órdenes de producción"),
        new(CommercialPermissionCodes.OrdersCreate, "commercial.production_orders", "create", "Crear órdenes de producción"),
        new(CommercialPermissionCodes.OrdersEditCommercial, "commercial.production_orders", "edit_commercial", "Editar información comercial"),
        new(CommercialPermissionCodes.OrdersSubmit, "commercial.production_orders", "submit", "Enviar órdenes a Producción"),
        new(CommercialPermissionCodes.OrdersDuplicate, "commercial.production_orders", "duplicate", "Duplicar órdenes de producción"),
        new(CommercialPermissionCodes.OrdersViewProduction, "commercial.production_orders", "view_production", "Ver información productiva"),
        new(CommercialPermissionCodes.OrdersSubmitForReview, "commercial.production_orders", "submit_for_review", "Enviar paquete a revisión comercial"),
        new(CommercialPermissionCodes.OrdersReview, "commercial.production_orders", "review", "Revisar y devolver paquetes comerciales"),
        new(CommercialPermissionCodes.OrdersEditProduction, "commercial.production_orders", "edit_production", "Diligenciar información productiva"),
        new(CommercialPermissionCodes.OrdersComplete, "commercial.production_orders", "complete", "Finalizar órdenes de producción"),
        new(CommercialPermissionCodes.OrdersManage, "commercial.production_orders", "manage", "Administrar todas las órdenes"),
        new(Portal.Domain.Commercial.Reports.ReportPermissionCodes.View, "commercial.reports", "view", "Ver reportes comerciales"),
        new(Portal.Domain.Commercial.Reports.ReportPermissionCodes.Edit, "commercial.reports", "edit", "Preparar y revisar reportes comerciales"),
        new(Portal.Domain.Commercial.Reports.ReportPermissionCodes.Export, "commercial.reports", "export", "Descargar reportes comerciales"),
    ];

    public async Task SeedAsync(
        DatabaseSeedSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IDbContextTransaction? transaction = null;

        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var permissions = await SynchronizePermissionsAsync(cancellationToken);
            var superadminRole = await EnsureSuperadminRoleAsync(cancellationToken);

            await EnsureSuperadminPermissionsAsync(
                superadminRole,
                permissions,
                cancellationToken);

            await EnsureCommercialRolesAsync(permissions, cancellationToken);

            var superadminUser = await EnsureSuperadminUserAsync(settings);

            await EnsureSuperadminUserRoleAsync(
                superadminUser,
                superadminRole,
                cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            logger.LogInformation(
                "Database seeding completed with {PermissionCount} official permissions.",
                permissions.Count);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<IReadOnlyList<Permission>> SynchronizePermissionsAsync(
        CancellationToken cancellationToken)
    {
        var codes = PermissionCodes.All.Concat(CommercialPermissionCodes.All).ToArray();
        var permissionsByCode = await dbContext.Permissions
            .Where(permission => codes.Contains(permission.Code))
            .ToDictionaryAsync(permission => permission.Code, StringComparer.Ordinal, cancellationToken);

        var now = timeProvider.GetUtcNow();

        foreach (var definition in PermissionDefinitions)
        {
            if (!permissionsByCode.TryGetValue(definition.Code, out var permission))
            {
                permission = new Permission
                {
                    Id = Guid.NewGuid(),
                    Code = definition.Code,
                    Module = definition.Module,
                    Action = definition.Action,
                    DisplayName = definition.DisplayName,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                dbContext.Permissions.Add(permission);
                permissionsByCode.Add(permission.Code, permission);
                continue;
            }

            if (permission.Module == definition.Module
                && permission.Action == definition.Action
                && permission.DisplayName == definition.DisplayName
                && permission.IsActive)
            {
                continue;
            }

            permission.Module = definition.Module;
            permission.Action = definition.Action;
            permission.DisplayName = definition.DisplayName;
            permission.IsActive = true;
            permission.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return PermissionDefinitions
            .Select(definition => permissionsByCode[definition.Code])
            .ToArray();
    }

    private async Task<ApplicationRole> EnsureSuperadminRoleAsync(
        CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(SuperadminRoleName);
        var now = timeProvider.GetUtcNow();

        if (role is null)
        {
            role = new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = SuperadminRoleName,
                Description = SuperadminRoleDescription,
                IsActive = true,
                IsSystem = true,
                CreatedAt = now,
                UpdatedAt = now,
            };

            EnsureIdentitySucceeded(
                await roleManager.CreateAsync(role),
                "create the Superadmin role");

            return role;
        }

        if (role.Name == SuperadminRoleName
            && role.Description == SuperadminRoleDescription
            && role.IsActive
            && role.IsSystem)
        {
            return role;
        }

        role.Name = SuperadminRoleName;
        role.Description = SuperadminRoleDescription;
        role.IsActive = true;
        role.IsSystem = true;
        role.UpdatedAt = now;

        EnsureIdentitySucceeded(
            await roleManager.UpdateAsync(role),
            "update the Superadmin role");

        return role;
    }

    private async Task EnsureSuperadminPermissionsAsync(
        ApplicationRole role,
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var permissionIds = permissions.Select(permission => permission.Id).ToArray();
        var assignedPermissionIds = await dbContext.RolePermissions
            .Where(rolePermission => rolePermission.RoleId == role.Id
                && permissionIds.Contains(rolePermission.PermissionId))
            .Select(rolePermission => rolePermission.PermissionId)
            .ToHashSetAsync(cancellationToken);

        var now = timeProvider.GetUtcNow();

        foreach (var permission in permissions)
        {
            if (assignedPermissionIds.Contains(permission.Id))
            {
                continue;
            }

            dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permission.Id,
                AssignedAt = now,
                AssignedByUserId = null,
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureCommercialRolesAsync(
        IReadOnlyCollection<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var agent = await roleManager.FindByNameAsync(CommercialAgentRoleName);
        var legacy = await roleManager.FindByNameAsync("Comercial");
        if (agent is not null && legacy is not null)
        {
            throw new InvalidOperationException(
                "Both 'Comercial' and 'Agente Comercial' roles exist. Review memberships before seeding; roles will not be merged automatically.");
        }

        if (agent is null && legacy is not null)
        {
            legacy.Name = CommercialAgentRoleName;
            legacy.Description = "Agente responsable de importar cotizaciones y preparar paquetes comerciales.";
            legacy.IsActive = true;
            legacy.IsSystem = true;
            legacy.UpdatedAt = timeProvider.GetUtcNow();
            EnsureIdentitySucceeded(await roleManager.UpdateAsync(legacy), "rename the Comercial role");
            agent = legacy;
        }

        agent ??= await EnsureSystemRoleAsync(
            CommercialAgentRoleName,
            "Agente responsable de importar cotizaciones y preparar paquetes comerciales.");
        var assistant = await EnsureSystemRoleAsync(
            CommercialAssistantRoleName,
            "Única responsable de revisar el paquete comercial antes de enviarlo a Producción.");

        await SynchronizeRolePermissionsAsync(agent, permissions,
        [
            CommercialPermissionCodes.OrdersView,
            CommercialPermissionCodes.OrdersCreate,
            CommercialPermissionCodes.OrdersEditCommercial,
            CommercialPermissionCodes.OrdersDuplicate,
            CommercialPermissionCodes.OrdersViewProduction,
            CommercialPermissionCodes.OrdersSubmitForReview,
        ], cancellationToken);
        await SynchronizeRolePermissionsAsync(assistant, permissions,
        [
            CommercialPermissionCodes.OrdersView,
            CommercialPermissionCodes.OrdersViewProduction,
            CommercialPermissionCodes.OrdersReview,
            CommercialPermissionCodes.OrdersSubmit,
            Portal.Domain.Commercial.Reports.ReportPermissionCodes.View,
            Portal.Domain.Commercial.Reports.ReportPermissionCodes.Edit,
            Portal.Domain.Commercial.Reports.ReportPermissionCodes.Export,
        ], cancellationToken);
    }

    private async Task<ApplicationRole> EnsureSystemRoleAsync(string name, string description)
    {
        var role = await roleManager.FindByNameAsync(name);
        var now = timeProvider.GetUtcNow();
        if (role is null)
        {
            role = new ApplicationRole
            {
                Id = Guid.NewGuid(), Name = name, Description = description,
                IsActive = true, IsSystem = true, CreatedAt = now, UpdatedAt = now,
            };
            EnsureIdentitySucceeded(await roleManager.CreateAsync(role), $"create the {name} role");
            return role;
        }

        role.Description = description;
        role.IsActive = true;
        role.IsSystem = true;
        role.UpdatedAt = now;
        EnsureIdentitySucceeded(await roleManager.UpdateAsync(role), $"update the {name} role");
        return role;
    }

    private async Task SynchronizeRolePermissionsAsync(
        ApplicationRole role,
        IReadOnlyCollection<Permission> permissions,
        IReadOnlyCollection<string> desiredCodes,
        CancellationToken cancellationToken)
    {
        var commercialPermissionIds = permissions
            .Where(permission => permission.Code.StartsWith("commercial.", StringComparison.Ordinal))
            .Select(permission => permission.Id)
            .ToHashSet();
        var desiredIds = permissions.Where(permission => desiredCodes.Contains(permission.Code)).Select(permission => permission.Id).ToHashSet();
        var existing = await dbContext.RolePermissions
            .Where(item => item.RoleId == role.Id && commercialPermissionIds.Contains(item.PermissionId))
            .ToArrayAsync(cancellationToken);
        dbContext.RolePermissions.RemoveRange(existing.Where(item => !desiredIds.Contains(item.PermissionId)));
        var existingIds = existing.Select(item => item.PermissionId).ToHashSet();
        foreach (var permissionId in desiredIds.Where(id => !existingIds.Contains(id)))
        {
            dbContext.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionId = permissionId,
                AssignedAt = timeProvider.GetUtcNow(),
                AssignedByUserId = null,
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ApplicationUser> EnsureSuperadminUserAsync(
        DatabaseSeedSettings settings)
    {
        var document = GetRequiredValue(
            settings.SuperadminDocument,
            "SUPERADMIN_DOCUMENT");

        EnsureMaximumLength(document, 256, "SUPERADMIN_DOCUMENT");

        var existingUser = await userManager.FindByNameAsync(document);

        if (existingUser is not null)
        {
            return existingUser;
        }

        var firstName = GetRequiredValue(
            settings.SuperadminFirstName,
            "SUPERADMIN_FIRST_NAME");
        var lastName = GetRequiredValue(
            settings.SuperadminLastName,
            "SUPERADMIN_LAST_NAME");
        var email = GetRequiredValue(
            settings.SuperadminEmail,
            "SUPERADMIN_EMAIL");

        EnsureMaximumLength(firstName, 120, "SUPERADMIN_FIRST_NAME");
        EnsureMaximumLength(lastName, 120, "SUPERADMIN_LAST_NAME");
        EnsureMaximumLength(email, 256, "SUPERADMIN_EMAIL");

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new InvalidOperationException(
                "The configured Superadmin email is already assigned to a different user.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = document,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            AreaId = null,
            AdvisorCode = null,
            IsActive = true,
            MustChangePassword = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        if (settings.InitialPassword is not null)
        {
            if (settings.InitialPassword.Length < 16
                || string.Equals(settings.InitialPassword, document, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The initial Superadmin password must be at least 16 characters and differ from the document.");
            }

            user.PasswordHash = passwordHasher.HashPassword(user, settings.InitialPassword);
        }
        else
        {
            // Legacy DEV bootstrap is preserved for existing local workflows.
            TemporaryCredential.SetDocumentBasedPassword(
                user,
                document,
                passwordHasher);
        }

        EnsureIdentitySucceeded(
            await userManager.CreateAsync(user),
            "create the initial Superadmin user");

        return user;
    }

    private async Task EnsureSuperadminUserRoleAsync(
        ApplicationUser user,
        ApplicationRole role,
        CancellationToken cancellationToken)
    {
        var assignmentExists = await dbContext.Set<ApplicationUserRole>()
            .AnyAsync(
                userRole => userRole.UserId == user.Id && userRole.RoleId == role.Id,
                cancellationToken);

        if (assignmentExists)
        {
            return;
        }

        dbContext.Set<ApplicationUserRole>().Add(new ApplicationUserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            AssignedAt = timeProvider.GetUtcNow(),
            AssignedByUserId = null,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string GetRequiredValue(string? value, string configurationKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{configurationKey} is required to create the initial Superadmin user.");
        }

        return value.Trim();
    }

    private static void EnsureMaximumLength(
        string value,
        int maximumLength,
        string configurationKey)
    {
        if (value.Length > maximumLength)
        {
            throw new InvalidOperationException(
                $"{configurationKey} cannot exceed {maximumLength} characters.");
        }
    }

    private static void EnsureIdentitySucceeded(
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errorCodes = string.Join(
            ", ",
            result.Errors.Select(error => error.Code).Distinct(StringComparer.Ordinal));

        throw new InvalidOperationException(
            $"Identity failed to {operation}. Error codes: {errorCodes}.");
    }

    private sealed record PermissionDefinition(
        string Code,
        string Module,
        string Action,
        string DisplayName);
}
