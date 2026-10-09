using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Portal.Domain.Areas;
using Portal.Domain.Auditing;
using Portal.Domain.Permissions;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Identity;

namespace Portal.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<
        ApplicationUser,
        ApplicationRole,
        Guid,
        IdentityUserClaim<Guid>,
        ApplicationUserRole,
        IdentityUserLogin<Guid>,
        IdentityRoleClaim<Guid>,
        IdentityUserToken<Guid>>(options)
{
    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();

    public DbSet<ProductionOrderDocument> ProductionOrderDocuments => Set<ProductionOrderDocument>();

    public DbSet<ProductionOrderImport> ProductionOrderImports => Set<ProductionOrderImport>();

    public DbSet<CommercialNotificationOutbox> CommercialNotificationOutbox => Set<CommercialNotificationOutbox>();

    public DbSet<Portal.Domain.Commercial.Reports.CommercialReport> CommercialReports => Set<Portal.Domain.Commercial.Reports.CommercialReport>();
    public DbSet<Portal.Domain.Commercial.Reports.CommercialReportSource> CommercialReportSources => Set<Portal.Domain.Commercial.Reports.CommercialReportSource>();
    public DbSet<Portal.Domain.Commercial.Reports.ProductionOrderReportRecord> OpReportRecords => Set<Portal.Domain.Commercial.Reports.ProductionOrderReportRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
