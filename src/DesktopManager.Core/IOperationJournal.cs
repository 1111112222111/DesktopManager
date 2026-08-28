namespace DesktopManager.Core;

public interface IOperationJournal
{
    Task SaveAsync(
        OrganizationOperation operation,
        CancellationToken cancellationToken = default);

    Task<OrganizationOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationOperation>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
