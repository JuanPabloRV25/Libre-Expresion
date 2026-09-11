using System.Collections.Concurrent;
using Portal.Application.Notifications;

namespace Portal.IntegrationTests;

public sealed class TestEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> messages = new();

    public bool ShouldFail { get; set; }

    public IReadOnlyCollection<EmailMessage> Messages => messages.ToArray();

    public Task SendAsync(
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (ShouldFail)
        {
            throw new InvalidOperationException("Controlled SMTP failure.");
        }

        messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
