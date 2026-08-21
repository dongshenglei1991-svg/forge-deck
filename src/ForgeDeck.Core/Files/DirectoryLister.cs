using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public sealed record FsEntry(string Name, string Path, bool IsDirectory, string Extension);
public sealed record FsListResult(string Path, IReadOnlyList<FsEntry> Entries);

public static class DirectoryLister
{
    public static FsListResult List(string path, string root)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (!Directory.Exists(fullPath))
            throw new BridgeException("not_found", "目录不存在");

        List<FsEntry> entries;
        try
        {
            entries = new List<FsEntry>();
            foreach (var info in new DirectoryInfo(fullPath).EnumerateFileSystemInfos())
            {
                try
                {
                    var isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    // .gitignore 等点文件：无「文件名」部分时整名会被当成扩展名，应视为无扩展名
                    var ext = isDir || string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(info.Name))
                        ? ""
                        : info.Extension.TrimStart('.').ToLowerInvariant();
                    entries.Add(new FsEntry(info.Name, info.FullName, isDir, ext));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // 单条失败跳过，不打爆整层
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            throw new BridgeException("io", "无法读取该目录");
        }
        catch (IOException)
        {
            throw new BridgeException("io", "无法读取该目录");
        }

        entries.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return new FsListResult(fullPath, entries);
    }
}
