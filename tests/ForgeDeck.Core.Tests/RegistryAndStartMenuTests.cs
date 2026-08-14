using ForgeDeck.Core.Scanning;
using Microsoft.Win32;

namespace ForgeDeck.Core.Tests;

public class RegistryAndStartMenuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private const string TestUninstallKey = @"Software\ForgeDeckTests\Uninstall";

    public RegistryAndStartMenuTests() => Directory.CreateDirectory(_dir);
    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\ForgeDeckTests", false); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void RegistrySource_MatchesKnownTool_ByDisplayNameAndIcon()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorApp"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", $"{exe},0");
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hits = source.Scan(new ScanContext(Array.Empty<string>())).ToList();
        var hit = Assert.Single(hits);
        Assert.Equal("Cursor", hit.Known!.Name);
        Assert.Equal("注册表", hit.SourceLabel);
        Assert.Equal(Path.GetFullPath(exe), hit.ExePath);
    }

    [Fact]
    public void RegistrySource_FallsBackToInstallLocation_WhenIconNotExecutable()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        var ico = Path.Combine(_dir, "cursor.ico");
        File.WriteAllText(ico, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorIco"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", ico);   // 指向 .ico：存在但非可执行 → 回落 InstallLocation
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
        Assert.Equal("Cursor", hit.Known!.Name);
        Assert.Equal("注册表", hit.SourceLabel);
        Assert.Equal(Path.GetFullPath(exe), hit.ExePath);
    }

    [Fact]
    public void RegistrySource_ToleratesNonStringRegistryValues()
    {
        var exe = Path.Combine(_dir, "Cursor.exe");
        File.WriteAllText(exe, "");
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\CursorBadIcon"))
        {
            key.SetValue("DisplayName", "Cursor Editor");
            key.SetValue("InstallLocation", _dir);
            key.SetValue("DisplayIcon", 5, RegistryValueKind.DWord);   // 畸形 REG_DWORD：不应抛 InvalidCastException
        }

        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
        Assert.Equal(Path.GetFullPath(exe), hit.ExePath);
    }

    [Fact]
    public void RegistrySource_SkipsUnrelatedEntries()
    {
        using (var key = Registry.CurrentUser.CreateSubKey($@"{TestUninstallKey}\RandomApp"))
        {
            key.SetValue("DisplayName", "Some Random Software");
        }
        var source = new RegistryScanSource(new RegistryUninstallRegistry(new[] { TestUninstallKey }));
        Assert.Empty(source.Scan(new ScanContext(Array.Empty<string>())));
    }

    [Fact]
    public void StartMenuSource_ResolvesLnkTarget()
    {
        var exe = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(exe, "");
        var lnkPath = Path.Combine(_dir, "Claude.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = exe;
        shortcut.Save();

        var resolver = new WScriptShellLinkResolver();
        Assert.Equal(exe, resolver.ResolveTarget(lnkPath));

        var menuDir = Path.Combine(_dir, "StartMenu");
        var subDir = Path.Combine(menuDir, "Sub");   // 子目录：覆盖递归枚举路径
        Directory.CreateDirectory(subDir);
        var lnk2 = Path.Combine(subDir, "Claude2.lnk");
        dynamic sc2 = shell.CreateShortcut(lnk2);
        sc2.TargetPath = exe;
        sc2.Save();
        var source = new StartMenuScanSourceForTest(resolver, new[] { menuDir });
        var hit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
        Assert.Equal("Claude Code", hit.Known!.Name);
        Assert.Equal("开始菜单", hit.SourceLabel);
    }
}

file sealed class StartMenuScanSourceForTest(IShellLinkResolver resolver, string[] dirs)
    : StartMenuScanSource(resolver)
{
    protected override string[] MenuDirs => dirs;
}
