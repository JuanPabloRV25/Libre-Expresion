namespace Portal.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public bool Enabled { get; init; }

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 25;

    public bool UseTls { get; init; }

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string FromName { get; init; } = string.Empty;

    public string FromAddress { get; init; } = string.Empty;

    public string PortalBaseUrl { get; init; } = string.Empty;

    public string EnvironmentLabel { get; init; } = string.Empty;
}
