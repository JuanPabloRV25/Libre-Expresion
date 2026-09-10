namespace Portal.Application.Identity;

public sealed record CurrentUserArea(Guid Id, string Name);

public sealed record CurrentUserInfo(
    Guid Id,
    string DocumentNumber,
    string FirstName,
    string LastName,
    string Email,
    CurrentUserArea? Area,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool IsActive,
    bool MustChangePassword);
