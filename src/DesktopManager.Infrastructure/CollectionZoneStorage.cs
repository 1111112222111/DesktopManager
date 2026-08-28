using DesktopManager.Core;
using Microsoft.VisualBasic.FileIO;

namespace DesktopManager.Infrastructure;

public sealed record CollectionWindowItem(
    string Path,
    string Name,
    DesktopItemKind Kind,
    long Size,
    DateTimeOffset ModifiedAt);

public sealed record CollectionFileOperationResult(
    string SourcePath,
    bool Succeeded,
    string? TargetPath = null,
    string? Error = null);

public sealed class CollectionZoneStorage
{
    private readonly string _protectedApplicationPath;

    public CollectionZoneStorage(string? protectedApplicationPath = null)
    {
        _protectedApplicationPath = Path.GetFullPath(
            protectedApplicationPath ?? AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public IReadOnlyList<CollectionWindowItem> Read(string zoneDirectory, int limit = 200)
    {
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        return new DirectoryInfo(zoneRoot)
            .EnumerateFileSystemInfos("*", System.IO.SearchOption.TopDirectoryOnly)
            .OrderByDescending(item => item is DirectoryInfo)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .Select(ToWindowItem)
            .ToArray();
    }

    public IReadOnlyList<CollectionFileOperationResult> MoveInto(
        string zoneDirectory,
        IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        return sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => TryMoveInto(zoneRoot, path))
            .ToArray();
    }

    public IReadOnlyList<CollectionFileOperationResult> CopyInto(
        string zoneDirectory,
        IEnumerable<string> sourcePaths)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        return sourcePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => TryCopyInto(zoneRoot, path))
            .ToArray();
    }

    public CollectionFileOperationResult Rename(
        string zoneDirectory,
        string itemPath,
        string newName)
    {
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        var source = EnsureDescendant(zoneRoot, itemPath);
        ValidateFileName(newName);
        var target = Path.Combine(Path.GetDirectoryName(source)!, newName.Trim());
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
        {
            return new CollectionFileOperationResult(source, true, source);
        }
        if (File.Exists(target) || Directory.Exists(target))
        {
            throw new IOException("收纳区中已存在同名项目。");
        }

        Move(source, target);
        return new CollectionFileOperationResult(source, true, target);
    }

    public CollectionFileOperationResult MoveOut(
        string zoneDirectory,
        string itemPath,
        string destinationDirectory)
    {
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        var source = EnsureDescendant(zoneRoot, itemPath);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destinationRoot))
        {
            throw new DirectoryNotFoundException("移出目标目录不存在。");
        }

        var target = CreateAvailablePath(destinationRoot, Path.GetFileName(source));
        Move(source, target);
        return new CollectionFileOperationResult(source, true, target);
    }

    public CollectionFileOperationResult DeleteToRecycleBin(
        string zoneDirectory,
        string itemPath)
    {
        var zoneRoot = EnsureZoneDirectory(zoneDirectory);
        var source = EnsureDescendant(zoneRoot, itemPath);
        if (File.Exists(source))
        {
            FileSystem.DeleteFile(
                source,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        }
        else
        {
            FileSystem.DeleteDirectory(
                source,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        }

        return new CollectionFileOperationResult(source, true);
    }

    private CollectionFileOperationResult TryMoveInto(string zoneRoot, string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        try
        {
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                throw new FileNotFoundException("拖入的项目已不存在。", source);
            }
            if (string.Equals(Path.GetDirectoryName(source), zoneRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new CollectionFileOperationResult(source, true, source);
            }
            if (Directory.Exists(source)
                && zoneRoot.StartsWith(
                    source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("不能把包含当前收纳区的目录移入自身。");
            }
            if (Directory.Exists(source)
                && (string.Equals(source, _protectedApplicationPath, StringComparison.OrdinalIgnoreCase)
                    || _protectedApplicationPath.StartsWith(
                        source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "该文件夹包含当前正在运行的桌面管理程序，不能拖入收纳窗口。");
            }

            var target = CreateAvailablePath(zoneRoot, Path.GetFileName(source));
            Move(source, target);
            return new CollectionFileOperationResult(source, true, target);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            return new CollectionFileOperationResult(source, false, Error: exception.Message);
        }
    }

    private CollectionFileOperationResult TryCopyInto(string zoneRoot, string sourcePath)
    {
        var source = Path.GetFullPath(sourcePath);
        try
        {
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                throw new FileNotFoundException("剪贴板中的项目已不存在。", source);
            }
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("重解析点项目不能粘贴到收纳窗口。");
            }
            if (Directory.Exists(source)
                && (string.Equals(source, zoneRoot, StringComparison.OrdinalIgnoreCase)
                    || zoneRoot.StartsWith(
                        source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("不能把包含当前收纳区的目录复制到自身。");
            }
            if (Directory.Exists(source)
                && (string.Equals(source, _protectedApplicationPath, StringComparison.OrdinalIgnoreCase)
                    || _protectedApplicationPath.StartsWith(
                        source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    "该文件夹包含当前正在运行的桌面管理程序，不能复制到收纳窗口。");
            }
            if (Directory.Exists(source))
            {
                EnsureDirectoryTreeHasNoReparsePoints(source);
            }

            var name = Path.GetFileName(source.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("不能复制磁盘根目录。");
            }
            var target = CreateAvailablePath(zoneRoot, name);
            Copy(source, target);
            return new CollectionFileOperationResult(source, true, target);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException
            or ArgumentException)
        {
            return new CollectionFileOperationResult(source, false, Error: exception.Message);
        }
    }

    private static string EnsureZoneDirectory(string zoneDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zoneDirectory);
        var root = Path.GetFullPath(zoneDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar)
            .Equals(root, StringComparison.OrdinalIgnoreCase) is true)
        {
            throw new InvalidOperationException("磁盘根目录不能作为收纳区。");
        }
        Directory.CreateDirectory(root);
        return root;
    }

    private static string EnsureDescendant(string zoneRoot, string itemPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemPath);
        var item = Path.GetFullPath(itemPath);
        if (!item.StartsWith(zoneRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能操作当前收纳区内的项目。");
        }
        if (!File.Exists(item) && !Directory.Exists(item))
        {
            throw new FileNotFoundException("收纳窗口项目已不存在。", item);
        }
        return item;
    }

    private static void ValidateFileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed is "." or ".."
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("请输入有效的新名称。");
        }
    }

    private static string CreateAvailablePath(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        if (!File.Exists(candidate) && !Directory.Exists(candidate))
        {
            return candidate;
        }

        var extension = Path.GetExtension(name);
        var stem = Path.GetFileNameWithoutExtension(name);
        for (var index = 1; index <= 10_000; index++)
        {
            candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException("无法为项目生成可用名称。");
    }

    private static void Move(string source, string target)
    {
        if (File.Exists(source))
        {
            FileSystem.MoveFile(source, target, overwrite: false);
        }
        else
        {
            FileSystem.MoveDirectory(source, target, overwrite: false);
        }
    }

    private static void Copy(string source, string target)
    {
        if (File.Exists(source))
        {
            File.Copy(source, target, overwrite: false);
        }
        else
        {
            FileSystem.CopyDirectory(source, target, overwrite: false);
        }
    }

    private static void EnsureDirectoryTreeHasNoReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("包含重解析点的文件夹不能粘贴到收纳窗口。");
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                directory,
                "*",
                System.IO.SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException("包含重解析点的文件夹不能粘贴到收纳窗口。");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static CollectionWindowItem ToWindowItem(FileSystemInfo item)
    {
        var kind = item switch
        {
            DirectoryInfo => DesktopItemKind.Folder,
            FileInfo when string.Equals(item.Extension, ".lnk", StringComparison.OrdinalIgnoreCase) =>
                DesktopItemKind.Shortcut,
            _ => DesktopItemKind.File
        };
        var size = item is FileInfo file ? file.Length : 0;
        return new CollectionWindowItem(
            item.FullName,
            item.Name,
            kind,
            size,
            item.LastWriteTimeUtc);
    }
}
