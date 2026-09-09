namespace Portal.Domain.Permissions;

public sealed class RolePermission
{
    public Guid RoleId { get; set; }

    public Guid PermissionId { get; set; }

    public DateTimeOffset AssignedAt { get; set; }

    public Guid? AssignedByUserId { get; set; }
}
