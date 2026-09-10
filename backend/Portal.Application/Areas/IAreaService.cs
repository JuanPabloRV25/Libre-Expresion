namespace Portal.Application.Areas;

public interface IAreaService
{
    Task<IReadOnlyList<AreaDto>> ListAsync(
        string? search,
        bool? isActive,
        CancellationToken cancellationToken = default);

    Task<AreaDto?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<AreaOperationResult> CreateAsync(
        CreateAreaCommand command,
        CancellationToken cancellationToken = default);

    Task<AreaOperationResult> UpdateAsync(
        Guid id,
        UpdateAreaCommand command,
        CancellationToken cancellationToken = default);

    Task<AreaOperationResult> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default);
}
