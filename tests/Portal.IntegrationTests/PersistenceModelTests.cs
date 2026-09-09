using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Portal.Domain.Areas;
using Portal.Domain.Auditing;
using Portal.Domain.Permissions;
using Portal.Infrastructure.Identity;
using Portal.Infrastructure.Persistence;

namespace Portal.IntegrationTests;

public sealed class PersistenceModelTests
{
    private readonly IModel _model = CreateModel();

    [Fact]
    public void Model_contains_only_the_expected_phase_one_tables()
    {
        var tableNames = _model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .ToHashSet(StringComparer.Ordinal);

        string[] expectedTables =
        [
            "Areas",
            "Permissions",
            "RolePermissions",
            "AuditEvents",
            "AspNetUsers",
            "AspNetRoles",
            "AspNetUserRoles",
            "AspNetUserClaims",
            "AspNetUserLogins",
            "AspNetUserTokens",
            "AspNetRoleClaims",
        ];

        Assert.All(expectedTables, table => Assert.Contains(table, tableNames));
        Assert.DoesNotContain("AspNetUserPasskeys", tableNames);
    }

    [Fact]
    public void Model_enforces_identity_and_domain_uniqueness()
    {
        var user = Entity<ApplicationUser>();
        var role = Entity<ApplicationRole>();
        var area = Entity<Area>();
        var permission = Entity<Permission>();

        Assert.False(user.FindProperty(nameof(ApplicationUser.UserName))!.IsNullable);
        Assert.False(user.FindProperty(nameof(ApplicationUser.NormalizedUserName))!.IsNullable);
        Assert.False(user.FindProperty(nameof(ApplicationUser.Email))!.IsNullable);
        Assert.False(user.FindProperty(nameof(ApplicationUser.NormalizedEmail))!.IsNullable);

        AssertUniqueIndex(user, nameof(ApplicationUser.NormalizedUserName));
        AssertUniqueIndex(user, nameof(ApplicationUser.NormalizedEmail));
        AssertUniqueIndex(role, nameof(ApplicationRole.NormalizedName));
        AssertUniqueIndex(area, nameof(Area.Name));
        AssertUniqueIndex(permission, nameof(Permission.Code));

        Assert.Equal(
            [nameof(ApplicationUserRole.UserId), nameof(ApplicationUserRole.RoleId)],
            Entity<ApplicationUserRole>().FindPrimaryKey()!.Properties.Select(property => property.Name));

        Assert.Equal(
            [nameof(RolePermission.RoleId), nameof(RolePermission.PermissionId)],
            Entity<RolePermission>().FindPrimaryKey()!.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Model_uses_the_documented_delete_behaviors()
    {
        AssertDeleteBehavior<ApplicationUser>(nameof(ApplicationUser.AreaId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<ApplicationUserRole>(nameof(ApplicationUserRole.UserId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<ApplicationUserRole>(nameof(ApplicationUserRole.RoleId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<ApplicationUserRole>(nameof(ApplicationUserRole.AssignedByUserId), DeleteBehavior.SetNull);
        AssertDeleteBehavior<RolePermission>(nameof(RolePermission.RoleId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<RolePermission>(nameof(RolePermission.PermissionId), DeleteBehavior.Restrict);
        AssertDeleteBehavior<RolePermission>(nameof(RolePermission.AssignedByUserId), DeleteBehavior.SetNull);
        AssertDeleteBehavior<AuditEvent>(nameof(AuditEvent.ActorUserId), DeleteBehavior.SetNull);
    }

    [Fact]
    public void Model_uses_PostgreSql_types_required_by_DM_001()
    {
        Assert.Equal("timestamptz", Entity<Area>().FindProperty(nameof(Area.CreatedAt))!.GetColumnType());
        Assert.Equal("timestamptz", Entity<ApplicationUser>().FindProperty(nameof(ApplicationUser.CreatedAt))!.GetColumnType());
        Assert.Equal("timestamptz", Entity<ApplicationRole>().FindProperty(nameof(ApplicationRole.CreatedAt))!.GetColumnType());
        Assert.Equal("timestamptz", Entity<ApplicationUserRole>().FindProperty(nameof(ApplicationUserRole.AssignedAt))!.GetColumnType());
        Assert.Equal("timestamptz", Entity<RolePermission>().FindProperty(nameof(RolePermission.AssignedAt))!.GetColumnType());
        Assert.Equal("timestamptz", Entity<AuditEvent>().FindProperty(nameof(AuditEvent.OccurredAt))!.GetColumnType());
        Assert.Equal("inet", Entity<AuditEvent>().FindProperty(nameof(AuditEvent.IpAddress))!.GetColumnType());
        Assert.Equal("jsonb", Entity<AuditEvent>().FindProperty(nameof(AuditEvent.Metadata))!.GetColumnType());
    }

    private IEntityType Entity<TEntity>() =>
        _model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"{typeof(TEntity).Name} is not mapped.");

    private static void AssertUniqueIndex(IEntityType entityType, string propertyName)
    {
        Assert.Contains(
            entityType.GetIndexes(),
            index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual([propertyName]));
    }

    private void AssertDeleteBehavior<TEntity>(string propertyName, DeleteBehavior expected)
    {
        var foreignKey = Entity<TEntity>().GetForeignKeys()
            .Single(key => key.Properties.Select(property => property.Name).SequenceEqual([propertyName]));

        Assert.Equal(expected, foreignKey.DeleteBehavior);
    }

    private static IModel CreateModel()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=portal_model_validation;Username=portal;Password=not-used")
            .Options;

        using var context = new ApplicationDbContext(options);
        return context.Model;
    }
}
