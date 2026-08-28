using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class DiagnosticBundleService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions CompactSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task ExportAsync(
        string packagePath,
        DiagnosticEnvironment environment,
        IReadOnlyList<DiagnosticEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(entries);
        var fullPath = Path.GetFullPath(packagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("诊断包路径没有父目录。"));
        var temporaryPath = $"{fullPath}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var file = new FileStream(
                temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 81920, useAsync: true))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
            {
                await WriteJsonAsync(archive, "diagnostics.json", environment, cancellationToken);
                await WriteEventsAsync(archive, entries, cancellationToken);
                await WriteTextAsync(
                    archive,
                    "README.txt",
                    "桌面管理诊断包\r\n\r\n包含运行环境摘要和最近的脱敏诊断事件。"
                    + "不包含文件内容、设置数据库或用户名。\r\n",
                    cancellationToken);
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

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
    }

    private static async Task WriteEventsAsync(
        ZipArchive archive,
        IReadOnlyList<DiagnosticEntry> entries,
        CancellationToken cancellationToken)
    {
        await using var stream = archive.CreateEntry("events.jsonl", CompressionLevel.Optimal).Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false);
        foreach (var entry in entries)
        {
            var sanitized = entry with
            {
                Category = DiagnosticPrivacy.Redact(entry.Category),
                Message = DiagnosticPrivacy.Redact(entry.Message),
                Details = DiagnosticPrivacy.Redact(entry.Details)
            };
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(sanitized, CompactSerializerOptions).AsMemory(),
                cancellationToken);
        }
    }

    private static async Task WriteTextAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        await using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
