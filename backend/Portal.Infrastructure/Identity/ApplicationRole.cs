using Microsoft.AspNetCore.Identity;

namespace Portal.Infrastructure.Identity;

public sealed class ApplicationRole : IdentityRole<Guid>
{
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
