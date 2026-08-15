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
        catch (Exception)
        {
            // 按单文件失败处理：ProgID 缺失（ArgumentNullException）、dynamic 绑定失败
            // （RuntimeBinderException）等环境异常不应冒泡废掉整个开始菜单源。
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
            // IgnoreInaccessible：跳过 ACL 拒绝的不可访问子目录，避免整源报废（被源级隔离静默吞掉）；
            // AttributesToSkip = 0 必须显式设置：默认 Hidden|System 会静默跳过隐藏属性的 .lnk，改变语义。
            var eo = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            };
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", eo))
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
