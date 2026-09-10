namespace Portal.Application.Auditing;

public interface IAuditService
{
    Task<AuditPageDto> ListAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
