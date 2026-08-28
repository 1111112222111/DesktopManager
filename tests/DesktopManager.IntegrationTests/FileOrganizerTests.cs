using DesktopManager.Core;
using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class FileOrganizerTests
{
    [Fact]
    public async Task Execute_PersistsSuccessfulMove_ThatANewOrganizerCanUndo()
    {
        using var testDirectory = TestDirectory.Create();
        var sourcePath = testDirectory.CreateFile(@"Desktop\notes.txt", "important notes");
        var targetPath = testDirectory.GetPath(@"Managed\Documents\notes.txt");
        var journalDirectory = testDirectory.GetPath("Journal");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(
                Guid.NewGuid(),
                sourcePath,
                targetPath,
                SuggestedAction.Archive,
                "文档归档")]);

        var operation = await new FileOrganizer(
            journalDirectory,
            testDirectory.GetPath("Desktop"),
            testDirectory.GetPath("Managed")).ExecuteAsync(plan);

        Assert.Equal(OperationStatus.Completed, operation.Status);
        Assert.False(File.Exists(sourcePath));
        Assert.Equal("important notes", await File.ReadAllTextAsync(targetPath));
        Assert.True(File.Exists(Path.Combine(journalDirectory, $"{operation.Id:N}.json")));

        var undoOperation = await new FileOrganizer(
            journalDirectory,
            testDirectory.GetPath("Desktop"),
            testDirectory.GetPath("Managed")).UndoAsync(operation.Id);

        Assert.Equal(OperationStatus.Completed, undoOperation.Status);
        Assert.Equal(OperationKind.Undo, undoOperation.Kind);
        Assert.Equal(operation.Id, undoOperation.ReversesOperationId);
        Assert.Equal("important notes", await File.ReadAllTextAsync(sourcePath));
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task Execute_WhenPlanEscapesAllowedRoots_RejectsBeforeMovingFile()
    {
        using var testDirectory = TestDirectory.Create();
        var allowedSourceRoot = testDirectory.GetPath("AllowedDesktop");
        var allowedTargetRoot = testDirectory.GetPath("AllowedManaged");
        var outsideSource = testDirectory.CreateFile(@"Outside\private.txt", "must stay here");
        var outsideTarget = testDirectory.GetPath(@"OutsideTarget\private.txt");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(
                Guid.NewGuid(),
                outsideSource,
                outsideTarget,
                SuggestedAction.Archive,
                "invalid plan")]);

        var organizer = new FileOrganizer(
            testDirectory.GetPath("Journal"),
            allowedSourceRoot,
            allowedTargetRoot);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            organizer.ExecuteAsync(plan));

        Assert.Contains("允许范围", exception.Message);
        Assert.Equal("must stay here", await File.ReadAllTextAsync(outsideSource));
        Assert.False(File.Exists(outsideTarget));
    }

    [Fact]
    public async Task Execute_WhenTargetNameExists_UsesSafeNameWithoutOverwriting()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourcePath = testDirectory.CreateFile(@"Desktop\notes.txt", "new content");
        var desiredTarget = testDirectory.CreateFile(@"Managed\notes.txt", "existing content");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(
                Guid.NewGuid(),
                sourcePath,
                desiredTarget,
                SuggestedAction.Archive,
                "文档归档")]);

        var operation = await new FileOrganizer(
            testDirectory.GetPath("Journal"),
            sourceRoot,
            targetRoot).ExecuteAsync(plan);

        var actualTarget = testDirectory.GetPath(@"Managed\notes (1).txt");
        Assert.Equal(OperationStatus.Completed, operation.Status);
        Assert.Equal("existing content", await File.ReadAllTextAsync(desiredTarget));
        Assert.Equal("new content", await File.ReadAllTextAsync(actualTarget));
        Assert.Equal(actualTarget, Assert.Single(operation.Items).TargetPath);
        Assert.False(File.Exists(sourcePath));
    }

    [Fact]
    public async Task RecoverInterrupted_WhenMoveCompletedBeforeStatusSaved_ReconcilesWithoutMovingFiles()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourcePath = testDirectory.GetPath(@"Desktop\notes.txt");
        var targetPath = testDirectory.CreateFile(@"Managed\notes.txt", "already moved");
        var journal = new JsonOperationJournal(testDirectory.GetPath("Journal"));
        var interrupted = new OrganizationOperation(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OperationStatus.Running,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            null,
            [new OperationItem(sourcePath, targetPath, OperationItemStatus.Pending, null)]);
        await journal.SaveAsync(interrupted);

        var recovered = await new FileOrganizer(journal, sourceRoot, targetRoot)
            .RecoverInterruptedAsync();

        var operation = Assert.Single(recovered);
        Assert.Equal(OperationStatus.Completed, operation.Status);
        Assert.Equal(OperationItemStatus.Succeeded, Assert.Single(operation.Items).Status);
        Assert.False(File.Exists(sourcePath));
        Assert.Equal("already moved", await File.ReadAllTextAsync(targetPath));
        Assert.Equal(OperationStatus.Completed, (await journal.GetAsync(operation.Id))!.Status);
    }

    [Fact]
    public async Task Execute_WhenManagedDirectoryIsInsideDesktop_RejectsBeforeMovingFile()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath(@"Desktop\Managed");
        var sourcePath = testDirectory.CreateFile(@"Desktop\notes.txt", "must stay here");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(
                Guid.NewGuid(),
                sourcePath,
                Path.Combine(targetRoot, "notes.txt"),
                SuggestedAction.Archive,
                "invalid managed directory")]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            var organizer = new FileOrganizer(
                testDirectory.GetPath("Journal"),
                sourceRoot,
                targetRoot);
            await organizer.ExecuteAsync(plan);
        });

        Assert.Contains("不能互相包含", exception.Message);
        Assert.Equal("must stay here", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task Execute_FolderPlan_MovesFolderAndCanUndoIt()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourceFolder = testDirectory.GetPath(@"Desktop\Project");
        Directory.CreateDirectory(sourceFolder);
        File.WriteAllText(Path.Combine(sourceFolder, "notes.txt"), "folder content");
        var targetFolder = testDirectory.GetPath(@"Managed\Projects\Project");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(
                Guid.NewGuid(),
                sourceFolder,
                targetFolder,
                SuggestedAction.Archive,
                "文件夹归档",
                ObservedKind: DesktopItemKind.Folder)]);
        var organizer = new FileOrganizer(
            testDirectory.GetPath("Journal"),
            sourceRoot,
            targetRoot);

        var operation = await organizer.ExecuteAsync(plan);

        Assert.Equal(OperationStatus.Completed, operation.Status);
        Assert.False(Directory.Exists(sourceFolder));
        Assert.Equal("folder content", File.ReadAllText(Path.Combine(targetFolder, "notes.txt")));

        var undone = await organizer.UndoAsync(operation.Id);

        Assert.Equal(OperationStatus.Completed, undone.Status);
        Assert.Equal(OperationKind.Undo, undone.Kind);
        Assert.True(Directory.Exists(sourceFolder));
        Assert.False(Directory.Exists(targetFolder));
    }

    [Fact]
    public void Inspect_WhenSourceIsMissing_ReturnsBlockingIssueWithoutCreatingTarget()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        Directory.CreateDirectory(sourceRoot);
        var targetPath = testDirectory.GetPath(@"Managed\Documents\missing.txt");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Draft,
            [new PlanItem(
                Guid.NewGuid(),
                testDirectory.GetPath(@"Desktop\missing.txt"),
                targetPath,
                SuggestedAction.Archive,
                "测试")]);

        var report = new FileOrganizer(
            testDirectory.GetPath("Journal"),
            sourceRoot,
            targetRoot).Inspect(plan);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Issues, issue => issue.Kind is PreflightIssueKind.SourceMissing);
        Assert.False(Directory.Exists(targetRoot));
    }

    [Fact]
    public void Inspect_WhenSourceIsExclusivelyLocked_ReturnsBlockingBusyIssue()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourcePath = testDirectory.CreateFile(@"Desktop\busy.txt", "busy");
        Directory.CreateDirectory(targetRoot);
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Draft,
            [new PlanItem(
                Guid.NewGuid(),
                sourcePath,
                testDirectory.GetPath(@"Managed\busy.txt"),
                SuggestedAction.Archive,
                "测试",
                4)]);
        using var locked = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        var report = new FileOrganizer(
            testDirectory.GetPath("Journal"),
            sourceRoot,
            targetRoot).Inspect(plan);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Issues, issue => issue.Kind is PreflightIssueKind.SourceBusy);
    }

    [Fact]
    public void Inspect_WhenFolderContainsRunningApplication_ReturnsBlockingIssue()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourceFolder = testDirectory.GetPath(@"Desktop\DesktopManagerProject");
        var runningApplication = testDirectory.CreateFile(
            @"Desktop\DesktopManagerProject\bin\DesktopManager.App.exe",
            "test executable");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Draft,
            [new PlanItem(
                Guid.NewGuid(),
                sourceFolder,
                testDirectory.GetPath(@"Managed\DesktopManagerProject"),
                SuggestedAction.Archive,
                "文件夹归档",
                ObservedKind: DesktopItemKind.Folder)]);

        var report = new FileOrganizer(
            new JsonOperationJournal(testDirectory.GetPath("Journal")),
            sourceRoot,
            targetRoot,
            runningApplication).Inspect(plan);

        Assert.False(report.CanProceed);
        Assert.Contains(report.Issues, issue =>
            issue.Kind is PreflightIssueKind.RunningApplicationContained);
        Assert.True(Directory.Exists(sourceFolder));
    }

    [Fact]
    public async Task Undo_WithSingleSelection_CreatesIndependentRecordAndLeavesOtherItemRecoverable()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var firstSource = testDirectory.CreateFile(@"Desktop\first.txt", "first");
        var secondSource = testDirectory.CreateFile(@"Desktop\second.txt", "second");
        var firstTarget = testDirectory.GetPath(@"Managed\first.txt");
        var secondTarget = testDirectory.GetPath(@"Managed\second.txt");
        var plan = new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [
                new PlanItem(Guid.NewGuid(), firstSource, firstTarget, SuggestedAction.Archive, "测试"),
                new PlanItem(Guid.NewGuid(), secondSource, secondTarget, SuggestedAction.Archive, "测试")
            ]);
        var journal = new JsonOperationJournal(testDirectory.GetPath("Journal"));
        var organizer = new FileOrganizer(journal, sourceRoot, targetRoot);
        var original = await organizer.ExecuteAsync(plan);

        var firstUndo = await organizer.UndoAsync(
            original.Id,
            new UndoRequest([firstTarget]));

        Assert.Equal(OperationKind.Undo, firstUndo.Kind);
        Assert.Equal(original.Id, firstUndo.ReversesOperationId);
        Assert.True(File.Exists(firstSource));
        Assert.True(File.Exists(secondTarget));
        Assert.Equal(OperationStatus.Completed, (await journal.GetAsync(original.Id))!.Status);

        var secondUndo = await organizer.UndoAsync(
            original.Id,
            new UndoRequest([secondTarget]));

        Assert.Equal(OperationStatus.Completed, secondUndo.Status);
        Assert.True(File.Exists(secondSource));
    }

    [Fact]
    public async Task Undo_WhenOriginalPathConflicts_SafeRenameNeverOverwritesExistingItem()
    {
        using var testDirectory = TestDirectory.Create();
        var sourceRoot = testDirectory.GetPath("Desktop");
        var targetRoot = testDirectory.GetPath("Managed");
        var sourcePath = testDirectory.CreateFile(@"Desktop\notes.txt", "archived");
        var targetPath = testDirectory.GetPath(@"Managed\notes.txt");
        var organizer = new FileOrganizer(
            testDirectory.GetPath("Journal"),
            sourceRoot,
            targetRoot);
        var original = await organizer.ExecuteAsync(new OrganizationPlan(
            Guid.NewGuid(),
            PlanStatus.Confirmed,
            [new PlanItem(Guid.NewGuid(), sourcePath, targetPath, SuggestedAction.Archive, "测试")]));
        File.WriteAllText(sourcePath, "new desktop item");

        var undo = await organizer.UndoAsync(
            original.Id,
            new UndoRequest(ConflictResolution: UndoConflictResolution.SafeRename));

        Assert.Equal(OperationStatus.Completed, undo.Status);
        Assert.Equal("new desktop item", File.ReadAllText(sourcePath));
        Assert.Equal("archived", File.ReadAllText(testDirectory.GetPath(@"Desktop\notes (1).txt")));
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "DesktopManager.Tests");
        private readonly string _path;

        private TestDirectory(string path)
        {
            _path = path;
        }

        public static TestDirectory Create()
        {
            var path = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TestDirectory(path);
        }

        public string GetPath(string relativePath) => Path.Combine(_path, relativePath);

        public string CreateFile(string relativePath, string contents)
        {
            var path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }

        public void Dispose()
        {
            var resolvedRoot = Path.GetFullPath(TestRoot) + Path.DirectorySeparatorChar;
            var resolvedPath = Path.GetFullPath(_path) + Path.DirectorySeparatorChar;
            if (Directory.Exists(_path) && resolvedPath.StartsWith(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
