using System.Globalization;
using DesktopManager.Core;
using Microsoft.Data.Sqlite;

namespace DesktopManager.Infrastructure;

public sealed class SqliteOperationJournal : IOperationJournal
{
    private readonly string _databasePath;

    public SqliteOperationJournal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
    }

    public async Task SaveAsync(
        OrganizationOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var saveOperation = connection.CreateCommand();
        saveOperation.Transaction = transaction;
        saveOperation.CommandText = """
            INSERT INTO operations
                (id, plan_id, status, started_at, completed_at, kind, reverses_operation_id)
            VALUES
                ($id, $planId, $status, $startedAt, $completedAt, $kind, $reversesOperationId)
            ON CONFLICT(id) DO UPDATE SET
                plan_id = excluded.plan_id,
                status = excluded.status,
                started_at = excluded.started_at,
                completed_at = excluded.completed_at,
                kind = excluded.kind,
                reverses_operation_id = excluded.reverses_operation_id;
            """;
        saveOperation.Parameters.AddWithValue("$id", operation.Id.ToString("N"));
        saveOperation.Parameters.AddWithValue("$planId", operation.PlanId.ToString("N"));
        saveOperation.Parameters.AddWithValue("$status", (int)operation.Status);
        saveOperation.Parameters.AddWithValue("$startedAt", operation.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        saveOperation.Parameters.AddWithValue(
            "$completedAt",
            operation.CompletedAt is null
                ? DBNull.Value
                : operation.CompletedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        saveOperation.Parameters.AddWithValue("$kind", (int)operation.Kind);
        saveOperation.Parameters.AddWithValue(
            "$reversesOperationId",
            operation.ReversesOperationId is null
                ? DBNull.Value
                : operation.ReversesOperationId.Value.ToString("N"));
        await saveOperation.ExecuteNonQueryAsync(cancellationToken);

        var deleteItems = connection.CreateCommand();
        deleteItems.Transaction = transaction;
        deleteItems.CommandText = "DELETE FROM operation_items WHERE operation_id = $operationId;";
        deleteItems.Parameters.AddWithValue("$operationId", operation.Id.ToString("N"));
        await deleteItems.ExecuteNonQueryAsync(cancellationToken);

        for (var index = 0; index < operation.Items.Length; index++)
        {
            var item = operation.Items[index];
            var saveItem = connection.CreateCommand();
            saveItem.Transaction = transaction;
            saveItem.CommandText = """
                INSERT INTO operation_items
                    (operation_id, ordinal, source_path, target_path, status, error)
                VALUES
                    ($operationId, $ordinal, $sourcePath, $targetPath, $status, $error);
                """;
            saveItem.Parameters.AddWithValue("$operationId", operation.Id.ToString("N"));
            saveItem.Parameters.AddWithValue("$ordinal", index);
            saveItem.Parameters.AddWithValue("$sourcePath", item.SourcePath);
            saveItem.Parameters.AddWithValue("$targetPath", item.TargetPath);
            saveItem.Parameters.AddWithValue("$status", (int)item.Status);
            saveItem.Parameters.AddWithValue("$error", item.Error is null ? DBNull.Value : item.Error);
            await saveItem.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<OrganizationOperation?> GetAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var getOperation = connection.CreateCommand();
        getOperation.CommandText = """
            SELECT plan_id, status, started_at, completed_at, kind, reverses_operation_id
            FROM operations
            WHERE id = $id;
            """;
        getOperation.Parameters.AddWithValue("$id", operationId.ToString("N"));

        Guid planId;
        OperationStatus status;
        DateTimeOffset startedAt;
        DateTimeOffset? completedAt;
        OperationKind kind;
        Guid? reversesOperationId;
        await using (var reader = await getOperation.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            planId = Guid.ParseExact(reader.GetString(0), "N");
            status = (OperationStatus)reader.GetInt32(1);
            startedAt = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);
            completedAt = reader.IsDBNull(3)
                ? null
                : DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture);
            kind = (OperationKind)reader.GetInt32(4);
            reversesOperationId = reader.IsDBNull(5)
                ? null
                : Guid.ParseExact(reader.GetString(5), "N");
        }

        var getItems = connection.CreateCommand();
        getItems.CommandText = """
            SELECT source_path, target_path, status, error
            FROM operation_items
            WHERE operation_id = $operationId
            ORDER BY ordinal;
            """;
        getItems.Parameters.AddWithValue("$operationId", operationId.ToString("N"));
        var items = new List<OperationItem>();
        await using (var reader = await getItems.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new OperationItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    (OperationItemStatus)reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        return new OrganizationOperation(
            operationId,
            planId,
            status,
            startedAt,
            completedAt,
            [.. items],
            kind,
            reversesOperationId);
    }

    public async Task<IReadOnlyList<OrganizationOperation>> ListAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        await using var connection = await OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM operations
            ORDER BY started_at DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var operationIds = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                operationIds.Add(Guid.ParseExact(reader.GetString(0), "N"));
            }
        }

        var operations = new List<OrganizationOperation>(operationIds.Count);
        foreach (var operationId in operationIds)
        {
            var operation = await GetAsync(operationId, cancellationToken);
            if (operation is not null)
            {
                operations.Add(operation);
            }
        }

        return operations;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException("数据库路径没有父目录。");
        Directory.CreateDirectory(directory);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await InitializeAsync(connection, cancellationToken);
        return connection;
    }

    private static async Task InitializeAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS operations (
                id TEXT PRIMARY KEY,
                plan_id TEXT NOT NULL,
                status INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NULL,
                kind INTEGER NOT NULL DEFAULT 0,
                reverses_operation_id TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS operation_items (
                operation_id TEXT NOT NULL,
                ordinal INTEGER NOT NULL,
                source_path TEXT NOT NULL,
                target_path TEXT NOT NULL,
                status INTEGER NOT NULL,
                error TEXT NULL,
                PRIMARY KEY (operation_id, ordinal),
                FOREIGN KEY (operation_id) REFERENCES operations(id) ON DELETE CASCADE
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(
            connection,
            "operations",
            "kind",
            "INTEGER NOT NULL DEFAULT 0",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "operations",
            "reverses_operation_id",
            "TEXT NULL",
            cancellationToken);
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var columns = connection.CreateCommand();
        columns.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await columns.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        await reader.DisposeAsync();

        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
