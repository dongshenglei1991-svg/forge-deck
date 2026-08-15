using Microsoft.Win32;

namespace ForgeDeck.Core.Scanning;

public sealed record RegistryEntry(string DisplayName, string InstallLocation, string DisplayIcon);

public interface IUninstallRegistry
{
    IEnumerable<RegistryEntry> Entries();
}

public sealed class RegistryUninstallRegistry : IUninstallRegistry
{
    private static readonly string[] DefaultKeyPaths =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    private readonly string[] _keyPaths;

    public RegistryUninstallRegistry() : this(DefaultKeyPaths) { }
    public RegistryUninstallRegistry(string[] keyPaths) => _keyPaths = keyPaths;

    public IEnumerable<RegistryEntry> Entries()
    {
        foreach (var keyPath in _keyPaths)
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey(keyPath);
                if (key == null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var item = key.OpenSubKey(sub);
                    if (item == null) continue;
                    yield return new RegistryEntry(
                        item.GetValue("DisplayName") as string ?? "",
                        item.GetValue("InstallLocation") as string ?? "",
                        item.GetValue("DisplayIcon") as string ?? "");   // as：畸形 REG_DWORD 等不抛 InvalidCast
                }
            }
    }
}

public sealed class RegistryScanSource(IUninstallRegistry registry) : IScanSource
{
    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var entry in registry.Entries())
        {
            if (entry.DisplayName.Length == 0) continue;
            var known = KnownTools.MatchByName(entry.DisplayName);
            if (known == null) continue;
            var exe = ResolveExe(entry, known);
            if (exe != null) yield return new ScanHit(exe, known, "注册表");
        }
    }

    private static string? ResolveExe(RegistryEntry entry, KnownTool known)
    {
        // DisplayIcon 常指向 .ico/.dll 资源（如 "app.exe,0" / "app.ico" / "imageres.dll,-101"），
        // 仅当其为可启动扩展名且 exe 名与已知工具一致时直取，否则回落 InstallLocation 探测。
        var icon = entry.DisplayIcon.Split(',')[0].Trim().Trim('"');
        if (icon.Length > 0 && File.Exists(icon)
            && PathSearch.CliExtensions.Contains(Path.GetExtension(icon), StringComparer.OrdinalIgnoreCase)
            && KnownTools.MatchByExeName(icon)?.Name == known.Name)
            return Path.GetFullPath(icon);

        if (entry.InstallLocation.Length > 0 && Directory.Exists(entry.InstallLocation))
        {
            var probed = PathSearch.Probe(entry.InstallLocation, known.ExeNames).FirstOrDefault();
            if (probed != null) return Path.GetFullPath(probed);
        }
        return null;
    }
}
