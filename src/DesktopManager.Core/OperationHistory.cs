namespace DesktopManager.Core;

public enum OperationScope
{
    Demo,
    RealDesktop
}

public sealed record OperationJournalSource(
    OperationScope Scope,
    IOperationJournal Journal);

public sealed record ScopedOrganizationOperation(
    OperationScope Scope,
    OrganizationOperation Operation);

public sealed class OperationHistory
{
    private readonly OperationJournalSource[] _sources;

    public OperationHistory(params OperationJournalSource[] sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Length == 0)
        {
            throw new ArgumentException("至少需要一个操作日志源。", nameof(sources));
        }

        foreach (var source in sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(source.Journal);
        }

        _sources = sources.ToArray();
    }

    public async Task<IReadOnlyList<ScopedOrganizationOperation>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "历史记录数量必须大于零。");
        }

        var queries = _sources.Select(async source =>
        {
            var operations = await source.Journal.ListAsync(limit, cancellationToken);
            return operations.Select(operation => new ScopedOrganizationOperation(
                source.Scope,
                operation));
        });
        var results = await Task.WhenAll(queries);

        return results
            .SelectMany(items => items)
            .OrderByDescending(item => item.Operation.StartedAt)
            .Take(limit)
            .ToArray();
    }
}
