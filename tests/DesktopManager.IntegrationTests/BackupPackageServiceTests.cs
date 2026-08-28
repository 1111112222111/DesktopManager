using System.IO.Compression;
using DesktopManager.Core;
using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class BackupPackageServiceTests
{
    [Fact]
    public async Task ExportThenRead_PreservesPortableSettingsAndHistory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"DesktopManager.BackupTests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var packagePath = Path.Combine(root, "sample.dmbak");
            var operation = new OrganizationOperation(
                Guid.NewGuid(), Guid.NewGuid(), OperationStatus.Completed,
                DateTimeOffset.Parse("2026-08-21T01:00:00Z"),
                DateTimeOffset.Parse("2026-08-21T01:00:01Z"),
                [new OperationItem("C:\\Desktop\\a.txt", "D:\\Managed\\a.txt", OperationItemStatus.Succeeded, null)]);
            var payload = new BackupPackage(
                new BackupManifest(BackupPackageFormat.CurrentVersion, DateTimeOffset.Parse("2026-08-21T02:00:00Z"), "test"),
                new BackupSettings(
                    "D:\\Managed",
                    [new OrganizationRule(Guid.NewGuid(), "文档", 10, [".txt"], "文档")],
                    NotificationPreferences.Default,
                    [new DesktopItemPreference("C:\\Desktop\\keep.txt", DesktopItemDisposition.Keep)],
                    new GlobalHotKeyBinding("Ctrl + Shift + F8", 0x0006, 0x77),
                    [new FavoriteCollection(Guid.NewGuid(), "工作", ["C:\\Desktop\\keep.txt"])]),
                [new ScopedOrganizationOperation(OperationScope.RealDesktop, operation)]);

            var service = new BackupPackageService();
            await service.ExportAsync(packagePath, payload);
            var restored = await service.ReadAsync(packagePath);

            Assert.Equal(payload.Manifest, restored.Manifest);
            Assert.Equal(payload.Settings.ManagedDirectory, restored.Settings.ManagedDirectory);
            Assert.Single(restored.Settings.Rules);
            Assert.Equal(payload.Settings.Rules[0].Id, restored.Settings.Rules[0].Id);
            Assert.Equal(payload.Settings.Rules[0].Name, restored.Settings.Rules[0].Name);
            Assert.Equal(payload.Settings.Rules[0].Extensions, restored.Settings.Rules[0].Extensions);
            Assert.Equal(payload.Settings.Notifications, restored.Settings.Notifications);
            Assert.Equal(payload.Settings.ItemPreferences, restored.Settings.ItemPreferences);
            Assert.Equal(payload.Settings.GlobalHotKeyBinding, restored.Settings.GlobalHotKeyBinding);
            var restoredFavorites = Assert.IsType<FavoriteCollection[]>(restored.Settings.Favorites);
            var expectedFavorites = Assert.IsType<FavoriteCollection[]>(payload.Settings.Favorites);
            Assert.Single(restoredFavorites);
            Assert.Equal(expectedFavorites[0].Name, restoredFavorites[0].Name);
            Assert.Equal(expectedFavorites[0].ItemPaths, restoredFavorites[0].ItemPaths);
            Assert.Single(restored.Operations);
            Assert.Equal(OperationScope.RealDesktop, restored.Operations[0].Scope);
            Assert.Equal(operation.Id, restored.Operations[0].Operation.Id);
            Assert.Equal(operation.Items, restored.Operations[0].Operation.Items);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Read_RejectsUnsupportedFutureFormat()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"DesktopManager.{Guid.NewGuid():N}.dmbak");
        try
        {
            var service = new BackupPackageService();
            await service.ExportAsync(packagePath, EmptyPackage(BackupPackageFormat.CurrentVersion + 1));

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(packagePath));

            Assert.Contains("版本", exception.Message);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    [Fact]
    public async Task Read_RejectsUnexpectedOrTraversingZipEntry()
    {
        var packagePath = Path.Combine(Path.GetTempPath(), $"DesktopManager.{Guid.NewGuid():N}.dmbak");
        try
        {
            var service = new BackupPackageService();
            await service.ExportAsync(packagePath, EmptyPackage(BackupPackageFormat.CurrentVersion));
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.CreateEntry("../outside.txt");
            }

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() => service.ReadAsync(packagePath));

            Assert.Contains("条目", exception.Message);
        }
        finally
        {
            File.Delete(packagePath);
        }
    }

    private static BackupPackage EmptyPackage(int formatVersion) => new(
        new BackupManifest(formatVersion, DateTimeOffset.UtcNow, "test"),
        new BackupSettings(null, [], NotificationPreferences.Default, []),
        []);
}
