using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class FileOrganizer
{
    private readonly IOperationJournal _journal;
    private readonly string _allowedSourceRoot;
    private readonly string _allowedTargetRoot;
    private readonly string _protectedApplicationPath;

    public FileOrganizer(
        string journalDirectory,
        string allowedSourceRoot,
        string allowedTargetRoot)
        : this(
            new JsonOperationJournal(journalDirectory),
            allowedSourceRoot,
            allowedTargetRoot)
    {
    }

    public FileOrganizer(
        IOperationJournal journal,
        string allowedSourceRoot,
        string allowedTargetRoot,
        string? protectedApplicationPath = null)
    {
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedSourceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(allowedTargetRoot);
        _journal = journal;
        _allowedSourceRoot = Path.GetFullPath(allowedSourceRoot);
        _allowedTargetRoot = Path.GetFullPath(allowedTargetRoot);
        _protectedApplicationPath = Path.GetFullPath(
            protectedApplicationPath ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureRootsDoNotOverlap();
    }

    public OrganizationPreflight Inspect(OrganizationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<PreflightIssue>();

        foreach (var item in plan.Items)
        {
            if (!IsWithinRoot(item.SourcePath, _allowedSourceRoot)
                || !IsWithinRoot(item.TargetPath, _allowedTargetRoot))
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.PathOutsideAuthorizedRoots,
                    PreflightIssueSeverity.Blocking,
                    "计划项目越过允许范围中的源目录或托管目录。",
                    item.DesktopItemId));
                continue;
            }

            if (item.SuggestedAction is not SuggestedAction.Archive)
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.UnsupportedAction,
                    PreflightIssueSeverity.Blocking,
                    $"当前执行器不接受 {item.SuggestedAction} 文件操作。",
                    item.DesktopItemId));
            }

            if (!Path.Exists(item.SourcePath))
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.SourceMissing,
                    PreflightIssueSeverity.Blocking,
                    "源项目已不存在。",
                    item.DesktopItemId));
                continue;
            }
            if (Directory.Exists(item.SourcePath)
                && (string.Equals(
                        Path.GetFullPath(item.SourcePath)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        _protectedApplicationPath,
                        StringComparison.OrdinalIgnoreCase)
                    || _protectedApplicationPath.StartsWith(
                        Path.GetFullPath(item.SourcePath)
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.RunningApplicationContained,
                    PreflightIssueSeverity.Blocking,
                    "该文件夹包含当前正在运行的桌面管理程序，已阻止收纳。",
                    item.DesktopItemId));
                continue;
            }

            try
            {
                var attributes = File.GetAttributes(item.SourcePath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add(new PreflightIssue(
                        PreflightIssueKind.ReparsePoint,
                        PreflightIssueSeverity.Blocking,
                        "源项目是符号链接、联接点或其他重解析点，当前版本拒绝移动。",
                        item.DesktopItemId));
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    InspectFileAvailability(item, issues);
                }
                else
                {
                    InspectDirectoryReparsePoints(item, issues);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.SourceBusy,
                    PreflightIssueSeverity.Blocking,
                    $"无法读取源项目状态：{exception.Message}",
                    item.DesktopItemId));
            }

            if (Path.Exists(item.TargetPath))
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.ExistingTarget,
                    PreflightIssueSeverity.Warning,
                    "目标位置已存在同名项目，执行时会使用安全名称。",
                    item.DesktopItemId));
            }
        }

        var summary = OrganizationPlanAnalyzer.Summarize(plan);
        if (summary.DuplicateTargetCount > 0)
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.DuplicateTarget,
                PreflightIssueSeverity.Warning,
                $"计划内有 {summary.DuplicateTargetCount} 项目标同名，执行时会依次使用安全名称。"));
        }
        if (summary.UnknownSizeItemCount > 0)
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.UnknownFolderSize,
                PreflightIssueSeverity.Warning,
                $"{summary.UnknownSizeItemCount} 个文件夹未递归计算大小，空间预检不包含其内容。"));
        }

        var availableSpace = InspectTargetRoot(summary, issues);
        return new OrganizationPreflight(issues, availableSpace);
    }

    public Task<OrganizationPreflight> InspectAsync(
        OrganizationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => Inspect(plan), cancellationToken);
    }

    public async Task<OrganizationOperation> ExecuteAsync(
        OrganizationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Status is not PlanStatus.Confirmed)
        {
            throw new InvalidOperationException("只有已确认的整理计划才能执行。");
        }

        var preflight = await InspectAsync(plan, cancellationToken).ConfigureAwait(false);
        if (!preflight.CanProceed)
        {
            throw new InvalidOperationException(
                $"执行预检存在 {preflight.BlockingCount} 个阻断项："
                + preflight.Issues.First(issue =>
                    issue.Severity is PreflightIssueSeverity.Blocking).Message);
        }

        EnsurePlanIsWithinAllowedRoots(plan.Items);

        var operation = new OrganizationOperation(
            Guid.NewGuid(),
            plan.Id,
            OperationStatus.Running,
            DateTimeOffset.UtcNow,
            null,
            plan.Items.Select(item => new OperationItem(
                item.SourcePath,
                item.TargetPath,
                OperationItemStatus.Pending,
                null)).ToArray());

        await _journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < plan.Items.Count; index++)
        {
            var planItem = plan.Items[index];
            try
            {
                var targetDirectory = Path.GetDirectoryName(planItem.TargetPath)
                    ?? throw new InvalidOperationException("目标路径没有父目录。");
                Directory.CreateDirectory(targetDirectory);
                var actualTargetPath = ResolveAvailableTargetPath(
                    planItem.TargetPath,
                    Directory.Exists(planItem.SourcePath));
                operation.Items[index] = operation.Items[index] with
                {
                    TargetPath = actualTargetPath
                };
                await _journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);

                await Task.Run(
                    () => MovePath(planItem.SourcePath, actualTargetPath, operation.Id),
                    cancellationToken).ConfigureAwait(false);
                operation.Items[index] = operation.Items[index] with
                {
                    Status = OperationItemStatus.Succeeded
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                operation.Items[index] = operation.Items[index] with
                {
                    Status = OperationItemStatus.Failed,
                    Error = exception.Message
                };
            }

            await _journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        operation = operation with
        {
            Status = GetCompletionStatus(operation.Items),
            CompletedAt = DateTimeOffset.UtcNow
        };
        await _journal.SaveAsync(operation, cancellationToken).ConfigureAwait(false);
        return operation;
    }

    public async Task<OrganizationOperation> UndoAsync(
        Guid operationId,
        UndoRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var original = await _journal.GetAsync(operationId, cancellationToken)
            ?? throw new FileNotFoundException("找不到指定的操作日志。", operationId.ToString("N"));
        if (original.Kind is not OperationKind.Organize)
        {
            throw new InvalidOperationException("撤销操作不能再次作为原整理操作撤销。 ");
        }
        EnsureOperationIsWithinAllowedRoots(original);
        request ??= new UndoRequest();

        var selectedTargets = request.OriginalTargetPaths?.Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var succeededOriginalItems = original.Items
            .Where(item => item.Status is OperationItemStatus.Succeeded)
            .ToArray();
        if (selectedTargets is { Count: > 0 }
            && !selectedTargets.IsSubsetOf(succeededOriginalItems
                .Select(item => Path.GetFullPath(item.TargetPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("只能撤销原整理操作中成功的项目。 ");
        }

        var previousOperations = await _journal.ListAsync(10_000, cancellationToken);
        var alreadyRestoredTargets = previousOperations
            .Where(operation => operation.Kind is OperationKind.Undo
                && operation.ReversesOperationId == operationId)
            .SelectMany(operation => operation.Items)
            .Where(item => item.Status is OperationItemStatus.Succeeded)
            .Select(item => Path.GetFullPath(item.SourcePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = succeededOriginalItems
            .Where(item => selectedTargets is null
                || selectedTargets.Contains(Path.GetFullPath(item.TargetPath)))
            .Where(item => !alreadyRestoredTargets.Contains(Path.GetFullPath(item.TargetPath)))
            .Reverse()
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new InvalidOperationException("所选项目已经撤销或当前不可撤销。 ");
        }
        if (request.ConflictResolution is UndoConflictResolution.AlternatePath
            && (candidates.Length != 1 || string.IsNullOrWhiteSpace(request.AlternateRestorePath)))
        {
            throw new InvalidOperationException("选择新恢复位置时必须且只能撤销一个项目。 ");
        }

        var undoItems = candidates.Select(item => CreateUndoItem(item, request)).ToArray();
        var undoOperation = new OrganizationOperation(
            Guid.NewGuid(),
            original.PlanId,
            OperationStatus.Running,
            DateTimeOffset.UtcNow,
            null,
            undoItems,
            OperationKind.Undo,
            original.Id);
        await _journal.SaveAsync(undoOperation, cancellationToken);

        for (var index = 0; index < undoOperation.Items.Length; index++)
        {
            var item = undoOperation.Items[index];
            if (item.Status is not OperationItemStatus.Pending)
            {
                continue;
            }

            try
            {
                var restoreDirectory = Path.GetDirectoryName(item.TargetPath)
                    ?? throw new InvalidOperationException("恢复路径没有父目录。");
                Directory.CreateDirectory(restoreDirectory);
                MovePath(item.SourcePath, item.TargetPath, undoOperation.Id);
                undoOperation.Items[index] = item with
                {
                    Status = OperationItemStatus.Succeeded,
                    Error = null
                };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                undoOperation.Items[index] = item with
                {
                    Status = OperationItemStatus.Failed,
                    Error = exception.Message
                };
            }
            await _journal.SaveAsync(undoOperation, cancellationToken);
        }

        undoOperation = undoOperation with
        {
            Status = GetCompletionStatus(undoOperation.Items),
            CompletedAt = DateTimeOffset.UtcNow
        };
        await _journal.SaveAsync(undoOperation, cancellationToken);
        return undoOperation;
    }

    public async Task<IReadOnlyList<OrganizationOperation>> RecoverInterruptedAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var operations = await _journal.ListAsync(limit, cancellationToken);
        var recovered = new List<OrganizationOperation>();

        foreach (var operation in operations.Where(item => item.Status is OperationStatus.Running))
        {
            EnsureOperationIsWithinAllowedRoots(operation);

            for (var index = 0; index < operation.Items.Length; index++)
            {
                var item = operation.Items[index];
                if (item.Status is not OperationItemStatus.Pending)
                {
                    continue;
                }

                CleanupInterruptedStagingPath(item.TargetPath, operation.Id);

                var sourceExists = Path.Exists(item.SourcePath);
                var targetExists = Path.Exists(item.TargetPath);
                operation.Items[index] = (!sourceExists, targetExists) switch
                {
                    (true, true) => item with
                    {
                        Status = OperationItemStatus.Succeeded,
                        Error = null
                    },
                    (false, false) => item with
                    {
                        Status = OperationItemStatus.Failed,
                        Error = "操作在文件移动前中断，未自动继续。"
                    },
                    (false, true) => item with
                    {
                        Status = OperationItemStatus.Failed,
                        Error = "源位置和目标位置同时存在，状态不明确。"
                    },
                    _ => item with
                    {
                        Status = OperationItemStatus.Failed,
                        Error = "源位置和目标位置均不存在，无法确认文件状态。"
                    }
                };
            }

            var reconciled = operation with
            {
                Status = GetCompletionStatus(operation.Items),
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _journal.SaveAsync(reconciled, cancellationToken);
            recovered.Add(reconciled);
        }

        return recovered;
    }

    private static string ResolveAvailableTargetPath(
        string desiredTargetPath,
        bool isDirectory = false)
    {
        if (!Path.Exists(desiredTargetPath))
        {
            return desiredTargetPath;
        }

        var directory = Path.GetDirectoryName(desiredTargetPath)
            ?? throw new InvalidOperationException("目标路径没有父目录。");
        var extension = isDirectory ? string.Empty : Path.GetExtension(desiredTargetPath);
        var baseName = isDirectory
            ? Path.GetFileName(desiredTargetPath)
            : Path.GetFileNameWithoutExtension(desiredTargetPath);

        for (var suffix = 1; suffix < int.MaxValue; suffix++)
        {
            var candidate = Path.Combine(directory, $"{baseName} ({suffix}){extension}");
            if (!Path.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法为目标项目生成可用的安全名称。");
    }

    private OperationItem CreateUndoItem(
        OperationItem originalItem,
        UndoRequest request)
    {
        var archivedPath = Path.GetFullPath(originalItem.TargetPath);
        var restorePath = request.ConflictResolution is UndoConflictResolution.AlternatePath
            ? Path.GetFullPath(request.AlternateRestorePath!)
            : Path.GetFullPath(originalItem.SourcePath);
        if (!IsWithinRoot(archivedPath, _allowedTargetRoot)
            || !IsWithinRoot(restorePath, _allowedSourceRoot))
        {
            throw new InvalidOperationException("撤销目标越过允许的桌面范围。 ");
        }
        if (!Path.Exists(archivedPath))
        {
            return new OperationItem(
                archivedPath,
                restorePath,
                OperationItemStatus.Failed,
                "已归档项目不存在，无法恢复。");
        }

        if (!Path.Exists(restorePath))
        {
            return new OperationItem(
                archivedPath,
                restorePath,
                OperationItemStatus.Pending,
                null);
        }

        return request.ConflictResolution switch
        {
            UndoConflictResolution.SafeRename => new OperationItem(
                archivedPath,
                ResolveAvailableTargetPath(restorePath, Directory.Exists(archivedPath)),
                OperationItemStatus.Pending,
                null),
            UndoConflictResolution.Skip => new OperationItem(
                archivedPath,
                restorePath,
                OperationItemStatus.Skipped,
                "用户选择跳过原位置冲突。"),
            _ => new OperationItem(
                archivedPath,
                restorePath,
                OperationItemStatus.Failed,
                "原位置已存在同名项目，未覆盖。")
        };
    }

    private static void MovePath(string sourcePath, string targetPath, Guid operationId)
    {
        if (Directory.Exists(sourcePath))
        {
            if (AreOnSameVolume(sourcePath, targetPath))
            {
                Directory.Move(sourcePath, targetPath);
            }
            else
            {
                MoveDirectoryAcrossVolumes(sourcePath, targetPath, operationId);
            }
            return;
        }
        File.Move(sourcePath, targetPath);
    }

    private static bool AreOnSameVolume(string firstPath, string secondPath) =>
        string.Equals(
            Path.GetPathRoot(Path.GetFullPath(firstPath)),
            Path.GetPathRoot(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);

    private static void MoveDirectoryAcrossVolumes(
        string sourcePath,
        string targetPath,
        Guid operationId)
    {
        var stagingPath = GetStagingPath(targetPath, operationId);
        if (Path.Exists(stagingPath))
        {
            throw new IOException("跨卷暂存路径已存在，拒绝覆盖。 ");
        }
        try
        {
            CopyDirectoryWithoutReparsePoints(sourcePath, stagingPath);
            Directory.Move(stagingPath, targetPath);
            Directory.Delete(sourcePath, recursive: true);
        }
        catch
        {
            if (Directory.Exists(stagingPath))
            {
                DeleteOwnedStagingDirectory(stagingPath);
            }
            throw;
        }
    }

    private static void CopyDirectoryWithoutReparsePoints(string sourcePath, string targetPath)
    {
        var sourceInfo = new DirectoryInfo(sourcePath);
        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("文件夹包含不允许复制的重解析点。 ");
        }
        Directory.CreateDirectory(targetPath);
        foreach (var entry in sourceInfo.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("文件夹内部包含重解析点，已停止跨卷归档。 ");
            }
            var destination = Path.Combine(targetPath, entry.Name);
            if (entry is DirectoryInfo directory)
            {
                CopyDirectoryWithoutReparsePoints(directory.FullName, destination);
            }
            else
            {
                File.Copy(entry.FullName, destination, overwrite: false);
            }
        }
        Directory.SetLastWriteTimeUtc(targetPath, sourceInfo.LastWriteTimeUtc);
    }

    private static string GetStagingPath(string targetPath, Guid operationId) =>
        targetPath + $".desktopmanager-staging-{operationId:N}";

    private static void CleanupInterruptedStagingPath(string targetPath, Guid operationId)
    {
        var stagingPath = GetStagingPath(targetPath, operationId);
        if (Directory.Exists(stagingPath) && !Path.Exists(targetPath))
        {
            DeleteOwnedStagingDirectory(stagingPath);
        }
    }

    private static void DeleteOwnedStagingDirectory(string stagingPath)
    {
        var root = new DirectoryInfo(stagingPath);
        if (ContainsReparsePoint(root))
        {
            throw new IOException("暂存目录包含重解析点，拒绝自动清理。 ");
        }
        Directory.Delete(stagingPath, recursive: true);
    }

    private static bool ContainsReparsePoint(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return true;
                }
                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
            }
        }
        return false;
    }

    private static void InspectFileAvailability(
        PlanItem item,
        ICollection<PreflightIssue> issues)
    {
        try
        {
            using var stream = new FileStream(
                item.SourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.SourceBusy,
                PreflightIssueSeverity.Blocking,
                $"源文件被占用或无法读取：{exception.Message}",
                item.DesktopItemId));
        }
    }

    private static void InspectDirectoryReparsePoints(
        PlanItem item,
        ICollection<PreflightIssue> issues)
    {
        var pending = new Stack<string>();
        pending.Push(item.SourcePath);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    issues.Add(new PreflightIssue(
                        PreflightIssueKind.ReparsePoint,
                        PreflightIssueSeverity.Blocking,
                        "文件夹内部包含符号链接、联接点或其他重解析点。",
                        item.DesktopItemId));
                    return;
                }
                if (entry is DirectoryInfo child)
                {
                    pending.Push(child.FullName);
                }
            }
        }
    }

    private long? InspectTargetRoot(
        OrganizationPlanSummary summary,
        ICollection<PreflightIssue> issues)
    {
        var existingDirectory = FindNearestExistingDirectory(_allowedTargetRoot);
        if (existingDirectory is null)
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.TargetVolumeUnavailable,
                PreflightIssueSeverity.Blocking,
                "托管目录所在介质不可用。"));
            return null;
        }

        if (!Directory.Exists(_allowedTargetRoot))
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.TargetDirectoryWillBeCreated,
                PreflightIssueSeverity.Warning,
                "托管目录当前不存在，执行时将在可用介质上创建。"));
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(existingDirectory).Take(1).ToArray();
            var root = Path.GetPathRoot(existingDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }
            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.TargetVolumeUnavailable,
                    PreflightIssueSeverity.Blocking,
                    "托管目录所在介质未就绪。"));
                return null;
            }
            var availableSpace = drive.AvailableFreeSpace;
            if (summary.KnownTotalSizeBytes > availableSpace)
            {
                issues.Add(new PreflightIssue(
                    PreflightIssueKind.InsufficientSpace,
                    PreflightIssueSeverity.Blocking,
                    "托管目录所在介质的可用空间小于计划的已知大小。"));
            }
            return availableSpace;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add(new PreflightIssue(
                PreflightIssueKind.TargetDirectoryInaccessible,
                PreflightIssueSeverity.Blocking,
                $"无法访问托管目录所在位置：{exception.Message}"));
            return null;
        }
    }

    private static string? FindNearestExistingDirectory(string path)
    {
        var candidate = Path.GetFullPath(path);
        while (!Directory.Exists(candidate))
        {
            var parent = Directory.GetParent(candidate);
            if (parent is null)
            {
                return null;
            }
            candidate = parent.FullName;
        }
        return candidate;
    }

    private void EnsurePlanIsWithinAllowedRoots(IEnumerable<PlanItem> items)
    {
        foreach (var item in items)
        {
            if (!IsWithinRoot(item.SourcePath, _allowedSourceRoot)
                || !IsWithinRoot(item.TargetPath, _allowedTargetRoot))
            {
                throw new InvalidOperationException("整理计划包含允许范围之外的路径，已拒绝执行。");
            }
        }
    }

    private void EnsureOperationIsWithinAllowedRoots(OrganizationOperation operation)
    {
        foreach (var item in operation.Items)
        {
            var sourceRoot = operation.Kind is OperationKind.Undo
                ? _allowedTargetRoot
                : _allowedSourceRoot;
            var targetRoot = operation.Kind is OperationKind.Undo
                ? _allowedSourceRoot
                : _allowedTargetRoot;
            if (!IsWithinRoot(item.SourcePath, sourceRoot)
                || !IsWithinRoot(item.TargetPath, targetRoot))
            {
                throw new InvalidOperationException("操作日志包含允许范围之外的路径，已拒绝撤销。");
            }
        }
    }

    private static bool IsWithinRoot(string candidatePath, string rootPath)
    {
        var rootPrefix = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(candidatePath);
        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRootsDoNotOverlap()
    {
        if (string.Equals(_allowedSourceRoot, _allowedTargetRoot, StringComparison.OrdinalIgnoreCase)
            || IsWithinRoot(_allowedSourceRoot, _allowedTargetRoot)
            || IsWithinRoot(_allowedTargetRoot, _allowedSourceRoot))
        {
            throw new InvalidOperationException("桌面目录与托管目录不能互相包含，也不能相同。");
        }
    }

    private static OperationStatus GetCompletionStatus(IReadOnlyCollection<OperationItem> items)
    {
        var succeeded = items.Count(item => item.Status is OperationItemStatus.Succeeded);
        var failed = items.Count(item => item.Status is OperationItemStatus.Failed);

        if (succeeded == items.Count)
        {
            return OperationStatus.Completed;
        }

        return succeeded > 0
            ? OperationStatus.PartiallyCompleted
            : OperationStatus.Failed;
    }
}
