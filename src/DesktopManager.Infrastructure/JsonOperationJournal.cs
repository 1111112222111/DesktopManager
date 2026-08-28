using System.Text.Json;
using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class JsonOperationJournal : IOperationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _journalDirectory;

    public JsonOperationJournal(string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        _journalDirectory = Path.GetFullPath(journalDirectory);
    }

    public async Task SaveAsync(
        OrganizationOperation operation,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_journalDirectory);
        var journalPath = GetJournalPath(operation.Id);
        var temporaryPath = journalPath + ".writing";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, operation, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, journalPath, overwrite: true);
    }

    public async Task<OrganizationOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var journalPath = GetJournalPath(operationId);
        if (!File.Exists(journalPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(journalPath);
        return await JsonSerializer.DeserializeAsync<OrganizationOperation>(
            stream,
            JsonOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<OrganizationOperation>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (!Directory.Exists(_journalDirectory))
        {
            return [];
        }

        var operations = new List<OrganizationOperation>();
        foreach (var journalPath in Directory.EnumerateFiles(_journalDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(journalPath);
            var operation = await JsonSerializer.DeserializeAsync<OrganizationOperation>(
                stream,
                JsonOptions,
                cancellationToken);
            if (operation is not null)
            {
                operations.Add(operation);
            }
        }

        return operations
            .OrderByDescending(operation => operation.StartedAt)
            .Take(limit)
            .ToArray();
    }

    private string GetJournalPath(Guid operationId) =>
        Path.Combine(_journalDirectory, $"{operationId:N}.json");
}
