using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class CollectionZoneStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "DesktopManager.CollectionZoneTests." + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MoveInto_WhenFolderContainsRunningApplication_LeavesFolderInPlace()
    {
        var sourceFolder = Directory.CreateDirectory(
            Path.Combine(_root, "Desktop", "DesktopManagerProject")).FullName;
        var binDirectory = Directory.CreateDirectory(Path.Combine(sourceFolder, "bin")).FullName;
        var applicationPath = Path.Combine(binDirectory, "DesktopManager.App.exe");
        File.WriteAllText(applicationPath, "test executable");
        var zoneDirectory = Path.Combine(_root, "Managed", "Files");
        var storage = new CollectionZoneStorage(applicationPath);

        var result = Assert.Single(storage.MoveInto(zoneDirectory, [sourceFolder]));

        Assert.False(result.Succeeded);
        Assert.Contains("正在运行", result.Error);
        Assert.True(Directory.Exists(sourceFolder));
        Assert.False(Directory.Exists(Path.Combine(zoneDirectory, "DesktopManagerProject")));
    }

    [Fact]
    public void MoveInto_Read_Rename_AndMoveOut_WorkAsOneInterface()
    {
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var zoneDirectory = Path.Combine(_root, "Managed", "文档");
        var outputDirectory = Directory.CreateDirectory(Path.Combine(_root, "Output")).FullName;
        var source = Path.Combine(sourceDirectory, "记录.txt");
        File.WriteAllText(source, "content");
        var storage = new CollectionZoneStorage();

        var move = Assert.Single(storage.MoveInto(zoneDirectory, [source]));
        Assert.True(move.Succeeded);
        Assert.False(File.Exists(source));
        var item = Assert.Single(storage.Read(zoneDirectory));
        Assert.Equal("记录.txt", item.Name);

        var renamed = storage.Rename(zoneDirectory, item.Path, "新记录.txt");
        Assert.True(File.Exists(renamed.TargetPath));

        var movedOut = storage.MoveOut(zoneDirectory, renamed.TargetPath!, outputDirectory);
        Assert.True(File.Exists(movedOut.TargetPath));
        Assert.Empty(storage.Read(zoneDirectory));
    }

    [Fact]
    public void MoveInto_UsesSafeNameWhenTargetAlreadyExists()
    {
        var firstRoot = Directory.CreateDirectory(Path.Combine(_root, "First")).FullName;
        var secondRoot = Directory.CreateDirectory(Path.Combine(_root, "Second")).FullName;
        var zoneDirectory = Path.Combine(_root, "Managed", "文档");
        var first = Path.Combine(firstRoot, "同名.txt");
        var second = Path.Combine(secondRoot, "同名.txt");
        File.WriteAllText(first, "first");
        File.WriteAllText(second, "second");
        var storage = new CollectionZoneStorage();

        Assert.True(Assert.Single(storage.MoveInto(zoneDirectory, [first])).Succeeded);
        Assert.True(Assert.Single(storage.MoveInto(zoneDirectory, [second])).Succeeded);

        Assert.Equal(["同名 (1).txt", "同名.txt"], storage.Read(zoneDirectory).Select(item => item.Name).Order().ToArray());
    }

    [Fact]
    public void CopyInto_CopiesFilesAndFoldersWithoutChangingSourcesAndUsesSafeNames()
    {
        var sourceRoot = Directory.CreateDirectory(Path.Combine(_root, "Clipboard")).FullName;
        var sourceFile = Path.Combine(sourceRoot, "资料.txt");
        File.WriteAllText(sourceFile, "content");
        var sourceFolder = Directory.CreateDirectory(Path.Combine(sourceRoot, "项目")).FullName;
        File.WriteAllText(Path.Combine(sourceFolder, "说明.md"), "folder content");
        var zoneDirectory = Path.Combine(_root, "Managed", "文档");
        var storage = new CollectionZoneStorage();

        var first = storage.CopyInto(zoneDirectory, [sourceFile, sourceFolder]);
        var second = storage.CopyInto(zoneDirectory, [sourceFile]);

        Assert.All(first, result => Assert.True(result.Succeeded, result.Error));
        Assert.True(Assert.Single(second).Succeeded);
        Assert.True(File.Exists(sourceFile));
        Assert.True(Directory.Exists(sourceFolder));
        Assert.True(File.Exists(Path.Combine(zoneDirectory, "资料.txt")));
        Assert.True(File.Exists(Path.Combine(zoneDirectory, "资料 (1).txt")));
        Assert.True(File.Exists(Path.Combine(zoneDirectory, "项目", "说明.md")));
    }

    [Fact]
    public void Rename_RejectsItemOutsideZone()
    {
        var zoneDirectory = Path.Combine(_root, "Managed", "文档");
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(_root, "Outside")).FullName;
        var outside = Path.Combine(outsideDirectory, "外部.txt");
        File.WriteAllText(outside, "content");
        var storage = new CollectionZoneStorage();

        Assert.Throws<InvalidOperationException>(() =>
            storage.Rename(zoneDirectory, outside, "非法.txt"));
    }

    [Fact]
    public void ReadAndRename_WorkInsideNestedFolder()
    {
        var zoneDirectory = Directory.CreateDirectory(Path.Combine(_root, "Managed", "文档")).FullName;
        var nestedDirectory = Directory.CreateDirectory(Path.Combine(zoneDirectory, "项目A")).FullName;
        var nestedFile = Path.Combine(nestedDirectory, "说明.txt");
        File.WriteAllText(nestedFile, "content");
        var storage = new CollectionZoneStorage();

        var item = Assert.Single(storage.Read(nestedDirectory));
        var renamed = storage.Rename(zoneDirectory, item.Path, "简介.txt");

        Assert.True(renamed.Succeeded);
        Assert.Equal(Path.Combine(nestedDirectory, "简介.txt"), renamed.TargetPath);
        Assert.True(File.Exists(renamed.TargetPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
