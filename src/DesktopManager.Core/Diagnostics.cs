namespace DesktopManager.Core;

public enum DiagnosticLevel
{
    Information,
    Warning,
    Error
}

public sealed record DiagnosticEntry(
    DateTimeOffset TimestampUtc,
    DiagnosticLevel Level,
    string Category,
    string Message,
    string? ExceptionType = null,
    string? Details = null);

public interface IDiagnosticLog
{
    void Write(
        DiagnosticLevel level,
        string category,
        string message,
        Exception? exception = null);

    IReadOnlyList<DiagnosticEntry> ReadRecent(int limit);
}

public sealed record DiagnosticEnvironment(
    string ApplicationVersion,
    string OperatingSystem,
    string RuntimeVersion,
    string ProcessArchitecture,
    DateTimeOffset ExportedAtUtc);
