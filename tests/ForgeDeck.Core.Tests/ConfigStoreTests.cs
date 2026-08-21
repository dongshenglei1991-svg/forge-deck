using ForgeDeck.Core;
using ForgeDeck.Core.Config;

namespace ForgeDeck.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private string PathFor(string name) => System.IO.Path.Combine(_dir, name);

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var store = new ConfigStore(PathFor("config.json"));
        store.Load();
        Assert.Equal(1, store.Config.Version);
        Assert.True(store.Config.Settings.AutoScanOnStartup);
        Assert.Empty(store.Config.Tools);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsProfile()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.Profiles.Add(new LaunchProfile
        {
            ToolId = "t1", Args = "--model x", Workdir = @"D:\work",
            Env = new() { ["A"] = "1" }, OpenMode = OpenMode.External, AutoRestore = true,
        });
        store.Save();

        var reloaded = new ConfigStore(path);
        reloaded.Load();
        var profile = Assert.Single(reloaded.Config.Profiles);
        Assert.Equal("t1", profile.ToolId);
        Assert.Equal(OpenMode.External, profile.OpenMode);
        Assert.True(profile.AutoRestore);
        Assert.Equal("1", profile.Env["A"]);
    }

    [Fact]
    public void Save_CreatesMissingDirectory_AndWritesNoTmpLeftover()
    {
        var path = PathFor("nested/deep/config.json");
        var store = new ConfigStore(path);
        store.Save();
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_BackupsAndReturnsDefaults()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, "{ not json !!!");
        var store = new ConfigStore(path);
        store.Load();
        Assert.Empty(store.Config.Tools);
        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Save_WritesCamelCaseContractForFrontend()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.Profiles.Add(new LaunchProfile { ToolId = "t1", OpenMode = OpenMode.External });
        store.Save();
        var json = File.ReadAllText(path);
        Assert.Contains("\"openMode\": \"external\"", json);
        Assert.Contains("\"toolId\": \"t1\"", json);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsHiddenPathsLastProfileAndPinned()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.HiddenExePaths.Add(@"C:\Tools\hidden.exe");
        store.Config.LastProfileByTool["t1"] = "p9";
        store.Config.Tools.Add(new ToolInfo { Id = "t1", Name = "X", ExePath = @"C:\Tools\x.exe", PathPinned = true });
        store.Save();

        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(@"C:\Tools\hidden.exe", Assert.Single(reloaded.Config.HiddenExePaths));
        Assert.Equal("p9", reloaded.Config.LastProfileByTool["t1"]);
        Assert.True(Assert.Single(reloaded.Config.Tools).PathPinned);
    }

    [Fact]
    public void Load_OldSettingsWithoutCloseBehavior_DefaultsToAsk()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, """{"version":1,"settings":{"defaultShell":"pwsh"}}""");
        var store = new ConfigStore(path);
        store.Load();
        Assert.Equal(CloseBehavior.Ask, store.Config.Settings.CloseBehavior);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsCloseBehavior()
    {
        var path = PathFor("config.json");
        var store = new ConfigStore(path);
        store.Config.Settings.CloseBehavior = CloseBehavior.MinimizeToTray;
        store.Save();
        Assert.Contains("\"closeBehavior\": \"minimizeToTray\"", File.ReadAllText(path));
        var reloaded = new ConfigStore(path);
        reloaded.Load();
        Assert.Equal(CloseBehavior.MinimizeToTray, reloaded.Config.Settings.CloseBehavior);
    }

    [Fact]
    public void Load_CorruptFile_BackupFails_StillReturnsDefaults()
    {
        var path = PathFor("config.json");
        File.WriteAllText(path, "{ not json !!!");
        Directory.CreateDirectory(path + ".bak"); // .bak 被目录占用 → File.Move 备份失败
        var store = new ConfigStore(path);
        store.Load();
        Assert.Empty(store.Config.Tools);
        Assert.True(File.Exists(path)); // 备份失败时保留原损坏文件，不崩溃
    }
}
