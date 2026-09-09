using Microsoft.AspNetCore.Identity;

namespace Portal.Infrastructure.Identity;

public sealed class ApplicationUserRole : IdentityUserRole<Guid>
{
    public DateTimeOffset AssignedAt { get; set; }

    public Guid? AssignedByUserId { get; set; }
}
