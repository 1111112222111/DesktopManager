using DesktopManager.Core;
using DesktopManager.Infrastructure;
using Microsoft.Data.Sqlite;

namespace DesktopManager.IntegrationTests;

public sealed class SqliteOperationJournalTests
{
    [Fact]
    public async Task SaveAndGet_PersistsOperationAcrossJournalInstances()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var databasePath = Path.Combine(root, "operations.db");
            var operation = new OrganizationOperation(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OperationStatus.Completed,
                new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 21, 10, 0, 1, TimeSpan.Zero),
                [new OperationItem(
                    Path.Combine(root, "Desktop", "notes.txt"),
                    Path.Combine(root, "Managed", "notes.txt"),
                    OperationItemStatus.Succeeded,
                    null)]);

            await new SqliteOperationJournal(databasePath).SaveAsync(operation);
            var restored = await new SqliteOperationJournal(databasePath).GetAsync(operation.Id);

            Assert.NotNull(restored);
            Assert.Equal(operation.Id, restored.Id);
            Assert.Equal(operation.PlanId, restored.PlanId);
            Assert.Equal(OperationStatus.Completed, restored.Status);
            var restoredItem = Assert.Single(restored.Items);
            Assert.Equal(operation.Items[0], restoredItem);
        }
        finally
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopManager.Tests"))
                + Path.DirectorySeparatorChar;
            var resolvedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (Directory.Exists(root) && resolvedRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task List_ReturnsNewestOperationsWithTheirItemsAcrossInstances()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var databasePath = Path.Combine(root, "operations.db");
            var older = CreateOperation(root, new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero), "older.txt");
            var newer = CreateOperation(root, new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero), "newer.txt");
            await new SqliteOperationJournal(databasePath).SaveAsync(older);
            await new SqliteOperationJournal(databasePath).SaveAsync(newer);

            var operations = await new SqliteOperationJournal(databasePath).ListAsync(10);

            Assert.Collection(
                operations,
                operation =>
                {
                    Assert.Equal(newer.Id, operation.Id);
                    Assert.EndsWith("newer.txt", Assert.Single(operation.Items).SourcePath);
                },
                operation =>
                {
                    Assert.Equal(older.Id, operation.Id);
                    Assert.EndsWith("older.txt", Assert.Single(operation.Items).SourcePath);
                });
        }
        finally
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopManager.Tests"))
                + Path.DirectorySeparatorChar;
            var resolvedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (Directory.Exists(root) && resolvedRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OperationHistory_MergesIndependentJournalsWithoutLosingScope()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var demoJournal = new SqliteOperationJournal(Path.Combine(root, "demo.db"));
            var realJournal = new SqliteOperationJournal(Path.Combine(root, "real.db"));
            var demoOperation = CreateOperation(
                root,
                new DateTimeOffset(2026, 8, 21, 9, 0, 0, TimeSpan.Zero),
                "demo.txt");
            var realOperation = CreateOperation(
                root,
                new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero),
                "real.txt");
            await demoJournal.SaveAsync(demoOperation);
            await realJournal.SaveAsync(realOperation);
            var history = new OperationHistory(
                new OperationJournalSource(OperationScope.Demo, demoJournal),
                new OperationJournalSource(OperationScope.RealDesktop, realJournal));

            var operations = await history.ListAsync(10);

            Assert.Collection(
                operations,
                item =>
                {
                    Assert.Equal(OperationScope.RealDesktop, item.Scope);
                    Assert.Equal(realOperation.Id, item.Operation.Id);
                },
                item =>
                {
                    Assert.Equal(OperationScope.Demo, item.Scope);
                    Assert.Equal(demoOperation.Id, item.Operation.Id);
                });
        }
        finally
        {
            var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopManager.Tests"))
                + Path.DirectorySeparatorChar;
            var resolvedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (Directory.Exists(root) && resolvedRoot.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAndGet_PersistsUndoRelationship()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var originalId = Guid.NewGuid();
            var operation = CreateOperation(root, DateTimeOffset.UtcNow, "restored.txt") with
            {
                Kind = OperationKind.Undo,
                ReversesOperationId = originalId
            };
            var journal = new SqliteOperationJournal(Path.Combine(root, "operations.db"));

            await journal.SaveAsync(operation);
            var restored = await journal.GetAsync(operation.Id);

            Assert.Equal(OperationKind.Undo, restored!.Kind);
            Assert.Equal(originalId, restored.ReversesOperationId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Get_UpgradesLegacyDatabaseWithOrganizeDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "operations.db");
            var operationId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            await using (var connection = new SqliteConnection(
                             $"Data Source={databasePath};Pooling=False"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE operations (
                        id TEXT PRIMARY KEY, plan_id TEXT NOT NULL, status INTEGER NOT NULL,
                        started_at TEXT NOT NULL, completed_at TEXT NULL);
                    CREATE TABLE operation_items (
                        operation_id TEXT NOT NULL, ordinal INTEGER NOT NULL,
                        source_path TEXT NOT NULL, target_path TEXT NOT NULL,
                        status INTEGER NOT NULL, error TEXT NULL,
                        PRIMARY KEY (operation_id, ordinal));
                    INSERT INTO operations VALUES ($id, $plan, 2, $started, $completed);
                    INSERT INTO operation_items VALUES ($id, 0, $source, $target, 1, NULL);
                    """;
                command.Parameters.AddWithValue("$id", operationId.ToString("N"));
                command.Parameters.AddWithValue("$plan", planId.ToString("N"));
                command.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$completed", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$source", Path.Combine(root, "Desktop", "a.txt"));
                command.Parameters.AddWithValue("$target", Path.Combine(root, "Managed", "a.txt"));
                await command.ExecuteNonQueryAsync();
            }

            var restored = await new SqliteOperationJournal(databasePath).GetAsync(operationId);

            Assert.Equal(OperationKind.Organize, restored!.Kind);
            Assert.Null(restored.ReversesOperationId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static OrganizationOperation CreateOperation(
        string root,
        DateTimeOffset startedAt,
        string fileName) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationStatus.Completed,
            startedAt,
            startedAt.AddSeconds(1),
            [new OperationItem(
                Path.Combine(root, "Desktop", fileName),
                Path.Combine(root, "Managed", fileName),
                OperationItemStatus.Succeeded,
                null)]);
}
