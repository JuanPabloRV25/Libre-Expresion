namespace Portal.Infrastructure.Persistence.Seeding;

public sealed record DatabaseSeedSettings(
    string? SuperadminDocument,
    string? SuperadminFirstName,
    string? SuperadminLastName,
    string? SuperadminEmail);
