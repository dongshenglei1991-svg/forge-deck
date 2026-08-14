using System.Runtime.InteropServices;

namespace ForgeDeck.Core.Scanning;

public interface IShellLinkResolver
{
    string? ResolveTarget(string lnkPath);
}

public sealed class WScriptShellLinkResolver : IShellLinkResolver
{
    public string? ResolveTarget(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            var target = (string)shortcut.TargetPath;
            return target.Length > 0 ? target : null;
        }
        catch (COMException)
        {
            return null;
        }
    }
}

public class StartMenuScanSource(IShellLinkResolver resolver) : IScanSource
{
    protected virtual string[] MenuDirs => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
    };

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var dir in MenuDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var target = resolver.ResolveTarget(lnk);
                if (target == null || !File.Exists(target)) continue;
                var known = KnownTools.MatchByExeName(target);
                if (known == null) continue;
                yield return new ScanHit(Path.GetFullPath(target), known, "开始菜单");
            }
        }
    }
}
