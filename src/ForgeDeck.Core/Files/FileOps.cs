using System.ComponentModel;
using System.Diagnostics;
using ForgeDeck.Core.Bridge;

namespace ForgeDeck.Core.Files;

public interface IShellOpener
{
    void OpenFile(string fullPath);
    void OpenDirectory(string fullPath);
}

public sealed class ProcessShellOpener : IShellOpener
{
    public void OpenFile(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new BridgeException("io", "无法打开该文件");
        }
    }

    public void OpenDirectory(string fullPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{fullPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new BridgeException("io", "无法打开该目录");
        }
    }
}

public static class FileOps
{
    public static void Open(string path, string root, IShellOpener opener)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (Directory.Exists(fullPath))
            throw new BridgeException("validation", "只能打开文件");
        if (!File.Exists(fullPath))
            throw new BridgeException("not_found", "文件不存在");
        opener.OpenFile(fullPath);
    }

    public static void OpenWithSystem(string path, string root, IShellOpener opener)
    {
        var (fullPath, _) = FsPaths.ResolveUnderRoot(path, root);
        if (Directory.Exists(fullPath))
        {
            opener.OpenDirectory(fullPath);
            return;
        }
        if (File.Exists(fullPath))
        {
            opener.OpenFile(fullPath);
            return;
        }
        throw new BridgeException("not_found", "路径不存在");
    }

    public static void Delete(string path, string root)
    {
        var (fullPath, fullRoot) = FsPaths.ResolveUnderRoot(path, root);
        if (fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new BridgeException("validation", "不能删除工作目录根");

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                return;
            }
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new BridgeException("io", "无法删除该路径");
        }

        throw new BridgeException("not_found", "路径不存在");
    }
}
