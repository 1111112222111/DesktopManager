using DesktopManager.Core;
using DesktopManager.Infrastructure;
using System.Diagnostics;
using Xunit.Abstractions;

namespace DesktopManager.IntegrationTests;

public sealed class DirectoryDesktopCatalogTests
{
    [Fact]
    public void CombinedCatalog_IncludesSecondaryItemsAsReadOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopManager.Tests", Guid.NewGuid().ToString("N"));
        var primary = Path.Combine(root, "Primary");
        var common = Path.Combine(root, "Common");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(common);
        File.WriteAllText(Path.Combine(primary, "mine.txt"), "mine");
        File.WriteAllText(Path.Combine(common, "shared.txt"), "shared");
        try
        {
            var snapshot = new CombinedDesktopCatalog(primary, readOnlyDirectory: common).GetSnapshot();

            Assert.Equal(2, snapshot.Items.Count);
            Assert.False(snapshot.Items.Single(item => item.Path.EndsWith("mine.txt")).IsReadOnly);
            Assert.True(snapshot.Items.Single(item => item.Path.EndsWith("shared.txt")).IsReadOnly);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CombinedCatalog_ApplyChangesRefreshesOnlyChangedTopLevelPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "DesktopManager.Tests", Guid.NewGuid().ToString("N"));
        var primary = Path.Combine(root, "Primary");
        Directory.CreateDirectory(primary);
        var unchangedPath = Path.Combine(primary, "unchanged.txt");
        var renamedFrom = Path.Combine(primary, "old.txt");
        var renamedTo = Path.Combine(primary, "new.txt");
        File.WriteAllText(unchangedPath, "same");
        File.WriteAllText(renamedFrom, "renamed");
        try
        {
            var catalog = new CombinedDesktopCatalog(primary);
            var before = catalog.GetSnapshot();
            var unchangedId = before.Items.Single(item => item.Path == unchangedPath).Id;
            File.Move(renamedFrom, renamedTo);

            var after = catalog.ApplyChanges(
                before,
                [new DesktopChange(DesktopChangeKind.Renamed, renamedTo, renamedFrom)]);

            Assert.DoesNotContain(after.Items, item => item.Path == renamedFrom);
            Assert.Contains(after.Items, item => item.Path == renamedTo);
            Assert.Equal(unchangedId, after.Items.Single(item => item.Path == unchangedPath).Id);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private readonly ITestOutputHelper _output;

    public DirectoryDesktopCatalogTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GetSnapshot_ReportsTopLevelFilesFoldersAndShortcuts()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "notes.txt"), "notes");
            File.WriteAllText(Path.Combine(root, "project.lnk"), "shortcut placeholder");
            Directory.CreateDirectory(Path.Combine(root, "Assets"));

            var snapshot = new DirectoryDesktopCatalog(root).GetSnapshot();

            Assert.Collection(
                snapshot.Items.OrderBy(item => item.Path),
                item => Assert.Equal(DesktopItemKind.Folder, item.Kind),
                item => Assert.Equal(DesktopItemKind.File, item.Kind),
                item => Assert.Equal(DesktopItemKind.Shortcut, item.Kind));
            Assert.All(snapshot.Items, item => Assert.Equal(Path.GetFullPath(item.Path), item.Path));
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
    public async Task ObserveChanges_WhenFileIsCreated_ReportsCreatedDesktopChange()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var expectedPath = Path.Combine(root, "new-note.txt");
            var observedChange = new TaskCompletionSource<DesktopChange>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var observation = new DirectoryDesktopCatalog(root).ObserveChanges(change =>
            {
                if (change.Kind is DesktopChangeKind.Created
                    && change.Path.Equals(expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    observedChange.TrySetResult(change);
                }
            });

            await File.WriteAllTextAsync(expectedPath, "new note");
            var change = await observedChange.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(DesktopChangeKind.Created, change.Kind);
            Assert.Equal(expectedPath, change.Path);
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
    public void GetSnapshot_ExcludesHiddenSystemAndIncompleteFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var visiblePath = Path.Combine(root, "ready.txt");
            var hiddenPath = Path.Combine(root, "hidden.txt");
            var systemPath = Path.Combine(root, "system.dat");
            File.WriteAllText(visiblePath, "ready");
            File.WriteAllText(hiddenPath, "hidden");
            File.WriteAllText(systemPath, "system");
            File.WriteAllText(Path.Combine(root, "download.crdownload"), "downloading");
            File.WriteAllText(Path.Combine(root, "archive.part"), "downloading");
            File.WriteAllText(Path.Combine(root, "scratch.tmp"), "temporary");
            File.WriteAllText(Path.Combine(root, "~$draft.docx"), "office temporary");
            File.SetAttributes(hiddenPath, File.GetAttributes(hiddenPath) | FileAttributes.Hidden);
            File.SetAttributes(systemPath, File.GetAttributes(systemPath) | FileAttributes.System);

            var snapshot = new DirectoryDesktopCatalog(root).GetSnapshot();

            var item = Assert.Single(snapshot.Items);
            Assert.Equal(visiblePath, item.Path);
        }
        finally
        {
            foreach (var path in Directory.EnumerateFiles(root))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

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
    public void GetSnapshot_WhenItemIsIgnored_ExcludesItFromSnapshot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var ignoredPath = Path.Combine(root, "ignored.txt");
            var visiblePath = Path.Combine(root, "visible.txt");
            File.WriteAllText(ignoredPath, "ignored");
            File.WriteAllText(visiblePath, "visible");
            var dispositions = DesktopItemDispositionPolicy.Empty.WithDisposition(
                ignoredPath,
                DesktopItemDisposition.Ignore);

            var snapshot = new DirectoryDesktopCatalog(root, dispositions).GetSnapshot();

            var item = Assert.Single(snapshot.Items);
            Assert.Equal(visiblePath, item.Path);
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
    [Trait("Category", "Performance")]
    public void GetSnapshot_WithOneThousandFiles_CompletesWithinTwoSeconds()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "DesktopManager.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            for (var index = 0; index < 1_000; index++)
            {
                File.WriteAllText(Path.Combine(root, $"item-{index:D4}.txt"), index.ToString());
            }

            var stopwatch = Stopwatch.StartNew();
            var snapshot = new DirectoryDesktopCatalog(root).GetSnapshot();
            stopwatch.Stop();

            _output.WriteLine("扫描 1000 个项目耗时：{0:N1}ms", stopwatch.Elapsed.TotalMilliseconds);

            Assert.Equal(1_000, snapshot.Items.Count);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(2),
                $"扫描耗时 {stopwatch.Elapsed.TotalMilliseconds:N0}ms，超过 2000ms 上限。");
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
}
