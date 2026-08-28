using System.IO.Compression;
using DesktopManager.Core;
using DesktopManager.Infrastructure;

namespace DesktopManager.IntegrationTests;

public sealed class FileDiagnosticLogTests
{
    [Fact]
    public void WriteThenRead_RedactsKnownPersonalRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), $"DesktopManager.Diagnostics.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var personalPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Desktop",
                "private.txt");
            var log = new FileDiagnosticLog(root);

            log.Write(DiagnosticLevel.Error, "Scan", $"无法读取 {personalPath}");
            var entry = Assert.Single(log.ReadRecent(10));

            Assert.DoesNotContain(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                entry.Message,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%USERPROFILE%", entry.Message);

            log.Write(DiagnosticLevel.Warning, "Scan", "无法读取 D:\\RedirectedDesktop\\secret.docx");
            var redirectedEntry = log.ReadRecent(10)[0];
            Assert.DoesNotContain("D:\\RedirectedDesktop", redirectedEntry.Message);
            Assert.Contains("[PATH]", redirectedEntry.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Export_CreatesMinimalSanitizedBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), $"DesktopManager.Diagnostics.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var output = Path.Combine(root, "diagnostics.zip");
            var environment = new DiagnosticEnvironment(
                "test", "Windows", ".NET", "X64", DateTimeOffset.UtcNow);
            var entries = new[]
            {
                new DiagnosticEntry(
                    DateTimeOffset.UtcNow,
                    DiagnosticLevel.Warning,
                    "HotKey",
                    $"无法读取 {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "private.txt")}")
            };

            await new DiagnosticBundleService().ExportAsync(output, environment, entries);

            using var archive = ZipFile.OpenRead(output);
            Assert.Equal(
                ["diagnostics.json", "events.jsonl", "README.txt"],
                archive.Entries.Select(entry => entry.FullName).OrderBy(name => name));
            var eventsEntry = archive.GetEntry("events.jsonl")!;
            using var reader = new StreamReader(eventsEntry.Open());
            var eventsText = await reader.ReadToEndAsync();
            Assert.Single(eventsText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                eventsText,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
