using System.Security;
using System.Text.Json;
using DesktopManager.Core;

namespace DesktopManager.Infrastructure;

public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly object _sync = new();
    private readonly string _directoryPath;
    private readonly TimeSpan _retention;

    public FileDiagnosticLog(string directoryPath, int retentionDays = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        if (retentionDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retentionDays));
        }
        _directoryPath = Path.GetFullPath(directoryPath);
        _retention = TimeSpan.FromDays(retentionDays);
    }

    public void Write(
        DiagnosticLevel level,
        string category,
        string message,
        Exception? exception = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentNullException.ThrowIfNull(message);
        var entry = new DiagnosticEntry(
            DateTimeOffset.UtcNow,
            level,
            DiagnosticPrivacy.Redact(category),
            DiagnosticPrivacy.Redact(message),
            exception?.GetType().FullName,
            DiagnosticPrivacy.Redact(exception?.ToString()));

        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(_directoryPath);
                DeleteExpiredFiles();
                var logPath = Path.Combine(
                    _directoryPath,
                    $"diagnostics-{entry.TimestampUtc:yyyyMMdd}.jsonl");
                File.AppendAllText(logPath, JsonSerializer.Serialize(entry, SerializerOptions) + Environment.NewLine);
            }
            catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException or SecurityException)
            {
                // 诊断记录不能反过来阻止主应用运行。
            }
        }
    }

    public IReadOnlyList<DiagnosticEntry> ReadRecent(int limit)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        lock (_sync)
        {
            if (!Directory.Exists(_directoryPath))
            {
                return [];
            }

            var entries = new List<DiagnosticEntry>();
            foreach (var path in Directory.GetFiles(_directoryPath, "diagnostics-*.jsonl")
                         .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadLines(path))
                {
                    try
                    {
                        var entry = JsonSerializer.Deserialize<DiagnosticEntry>(line, SerializerOptions);
                        if (entry is not null)
                        {
                            entries.Add(entry);
                        }
                    }
                    catch (JsonException)
                    {
                        // 单条损坏不应阻止读取其余诊断事件。
                    }
                }
            }

            return entries
                .OrderByDescending(entry => entry.TimestampUtc)
                .Take(limit)
                .ToArray();
        }
    }

    private void DeleteExpiredFiles()
    {
        var cutoff = DateTime.UtcNow - _retention;
        foreach (var path in Directory.GetFiles(_directoryPath, "diagnostics-*.jsonl"))
        {
            var item = new FileInfo(path);
            if (item.LastWriteTimeUtc < cutoff
                && !item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                item.Delete();
            }
        }
    }
}
