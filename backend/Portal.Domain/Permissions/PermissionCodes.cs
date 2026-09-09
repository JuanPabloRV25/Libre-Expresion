namespace Portal.Domain.Permissions;

public static class PermissionCodes
{
    public const string AreasView = "areas.view";
    public const string AreasCreate = "areas.create";
    public const string AreasEdit = "areas.edit";
    public const string AreasActivate = "areas.activate";

    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersEdit = "users.edit";
    public const string UsersActivate = "users.activate";
    public const string UsersAssignRoles = "users.assign_roles";
    public const string UsersResetPassword = "users.reset_password";

    public const string RolesView = "roles.view";
    public const string RolesCreate = "roles.create";
    public const string RolesEdit = "roles.edit";
    public const string RolesActivate = "roles.activate";
    public const string RolesAssignPermissions = "roles.assign_permissions";

    public const string PermissionsView = "permissions.view";

    public const string AuditView = "audit.view";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(
    [
        AreasView,
        AreasCreate,
        AreasEdit,
        AreasActivate,
        UsersView,
        UsersCreate,
        UsersEdit,
        UsersActivate,
        UsersAssignRoles,
        UsersResetPassword,
        RolesView,
        RolesCreate,
        RolesEdit,
        RolesActivate,
        RolesAssignPermissions,
        PermissionsView,
        AuditView,
    ]);
}
