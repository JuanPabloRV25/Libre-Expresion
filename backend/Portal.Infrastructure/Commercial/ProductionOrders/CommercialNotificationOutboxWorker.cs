using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Portal.Application.Notifications;
using Portal.Domain.Commercial.ProductionOrders;
using Portal.Infrastructure.Notifications;
using Portal.Infrastructure.Persistence;

namespace Portal.Infrastructure.Commercial.ProductionOrders;

public sealed class CommercialNotificationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<EmailOptions> options,
    ILogger<CommercialNotificationOutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(240),
    ];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Commercial notification outbox cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), timeProvider, stoppingToken);
        }
    }

    internal async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var now = timeProvider.GetUtcNow();
        var item = await db.CommercialNotificationOutbox
            .Where(candidate => candidate.Status == CommercialOutboxStatus.Pending && candidate.NextAttemptAt <= now)
            .OrderBy(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (item is null) return;

        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == item.RecipientUserId, cancellationToken);
        var order = await db.ProductionOrders.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == item.ProductionOrderId, cancellationToken);
        if (recipient?.Email is null || order is null)
        {
            item.Status = CommercialOutboxStatus.Failed;
            item.LastError = "Recipient or order is unavailable.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!IsCurrentNotification(item, order))
        {
            item.Status = CommercialOutboxStatus.Failed;
            item.LastError = "Superseded: la orden ya no corresponde a esta solicitud de revisión o corrección.";
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        item.Attempts++;
        item.NextAttemptAt = now + RetryDelays[Math.Min(item.Attempts - 1, RetryDelays.Length - 1)];
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            if (!options.Value.Enabled) throw new InvalidOperationException("Email delivery is disabled.");
            await emailSender.SendAsync(CreateMessage(item, recipient.Email, order), cancellationToken);
            item.Status = CommercialOutboxStatus.Sent;
            item.SentAt = timeProvider.GetUtcNow();
            item.LastError = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            item.LastError = Truncate(exception.GetType().Name + ": " + exception.Message, 1000);
            if (item.Attempts >= RetryDelays.Length) item.Status = CommercialOutboxStatus.Failed;
            logger.LogWarning(exception, "Commercial notification {OutboxId} failed on attempt {Attempt}.", item.Id, item.Attempts);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private EmailMessage CreateMessage(CommercialNotificationOutbox item, string email, ProductionOrder order)
    {
        var code = $"OP-{order.CreatedAt.Year}-{order.Consecutive:00000}";
        var link = new Uri(new Uri(options.Value.PortalBaseUrl.TrimEnd('/') + "/"), $"commercial/production-orders/{order.Id}").ToString();
        var encodedCode = HtmlEncoder.Default.Encode(code);
        var encodedClient = HtmlEncoder.Default.Encode(order.ClientName ?? "Sin cliente");
        var encodedLink = HtmlEncoder.Default.Encode(link);
        var payload = JsonSerializer.Deserialize<OutboxPayload>(item.PayloadJson);
        if (item.NotificationType == "commercial-correction-required")
        {
            var reason = payload?.Reason ?? "Consulta la observación en el Portal.";
            return new EmailMessage(
                email,
                $"[{options.Value.EnvironmentLabel}] Corrección requerida para {code}",
                $"<p>La orden <strong>{encodedCode}</strong> fue devuelta para corrección.</p><p>Motivo: {HtmlEncoder.Default.Encode(reason)}</p><p><a href=\"{encodedLink}\">Abrir la orden en el Portal</a></p>",
                $"La orden {code} fue devuelta para corrección. Motivo: {reason}\n{link}");
        }

        return new EmailMessage(
            email,
            $"[{options.Value.EnvironmentLabel}] Nueva orden para revisión: {code}",
            $"<p>Tienes una nueva orden para revisión comercial.</p><p><strong>{encodedCode}</strong><br>Cliente: {encodedClient}</p><p><a href=\"{encodedLink}\">Revisar en el Portal</a></p>",
            $"Tienes una nueva orden para revisión comercial. {code}. Cliente: {order.ClientName ?? "Sin cliente"}.\n{link}");
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    internal static bool IsCurrentNotification(CommercialNotificationOutbox item, ProductionOrder order)
    {
        var payload = JsonSerializer.Deserialize<OutboxPayload>(item.PayloadJson);
        if (order.CurrentAssigneeUserId != item.RecipientUserId) return false;
        if (item.NotificationType == "commercial-review-requested")
            return order.Status == ProductionOrderStatus.PendingCommercialReview
                && order.ReviewOwnerUserId == item.RecipientUserId
                && (payload?.OrderVersion is int version ? order.Version == version
                    : SameInstant(order.ReviewSubmittedAt, item.CreatedAt));
        if (item.NotificationType == "commercial-correction-required")
            return order.Status == ProductionOrderStatus.CorrectionRequired
                && SameInstant(order.ReviewReturnedAt, payload?.ReviewReturnedAt ?? item.CreatedAt);
        return false;
    }

    // PostgreSQL conserva microsegundos; el JSON puede conservar un tick adicional.
    private static bool SameInstant(DateTimeOffset? left, DateTimeOffset right) =>
        left.HasValue && Math.Abs((left.Value - right).Ticks) < 10;

    private sealed record OutboxPayload(Guid ActorUserId, string? Reason, int? OrderVersion = null,
        DateTimeOffset? ReviewSubmittedAt = null, DateTimeOffset? ReviewReturnedAt = null);
}
