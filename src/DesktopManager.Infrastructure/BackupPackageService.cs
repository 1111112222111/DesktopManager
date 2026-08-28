using System.IO.Compression;
using System.Text.Json;
using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class BackupPackageService
{
    private const long MaximumEntrySize = 50 * 1024 * 1024;
    private static readonly HashSet<string> ExpectedEntries = new(StringComparer.Ordinal)
    {
        "manifest.json",
        "settings.json",
        "operations.json"
    };
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task ExportAsync(
        string packagePath,
        BackupPackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(package);

        var fullPath = Path.GetFullPath(packagePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("备份路径没有父目录。");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, useAsync: true))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteJsonAsync(archive, "manifest.json", package.Manifest, cancellationToken);
                await WriteJsonAsync(archive, "settings.json", package.Settings, cancellationToken);
                await WriteJsonAsync(archive, "operations.json", package.Operations, cancellationToken);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<BackupPackage> ReadAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        await using var stream = new FileStream(
            Path.GetFullPath(packagePath), FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        ValidateEntries(archive);
        var manifest = await ReadJsonAsync<BackupManifest>(archive, "manifest.json", cancellationToken);
        if (manifest.FormatVersion != BackupPackageFormat.CurrentVersion)
        {
            throw new InvalidDataException(
                $"不支持备份包格式版本 {manifest.FormatVersion}；当前仅支持版本 {BackupPackageFormat.CurrentVersion}。");
        }
        var settings = await ReadJsonAsync<BackupSettings>(archive, "settings.json", cancellationToken);
        var operations = await ReadJsonAsync<ScopedOrganizationOperation[]>(
            archive, "operations.json", cancellationToken);
        return new BackupPackage(manifest, settings, operations);
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        if (archive.Entries.Count != ExpectedEntries.Count)
        {
            throw new InvalidDataException("备份包包含缺失、重复或非预期条目。");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!ExpectedEntries.Contains(entry.FullName)
                || !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal)
                || !names.Add(entry.FullName))
            {
                throw new InvalidDataException($"备份包包含不安全或非预期条目：{entry.FullName}");
            }

            if (entry.Length > MaximumEntrySize)
            {
                throw new InvalidDataException($"备份包条目过大：{entry.FullName}");
            }
        }
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
    }

    private static async Task<T> ReadJsonAsync<T>(
        ZipArchive archive,
        string entryName,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"备份包缺少 {entryName}。");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
            ?? throw new InvalidDataException($"备份包中的 {entryName} 内容为空。");
    }
}
