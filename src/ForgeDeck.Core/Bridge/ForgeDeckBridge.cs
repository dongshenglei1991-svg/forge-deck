using System.Text.Json;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Launching;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Bridge;

public sealed record ToolListItem(ToolInfo Tool, bool Exists, OpenMode DefaultMode);
public sealed record CommonDir(string Name, string Path);
public sealed record AppInfo(string Version, string UserName, DateTime? LastScanAt, LastUsedInfo? LastUsed);
public sealed record SettingsInfo(AppSettings Settings, IReadOnlyList<CommonDir> CommonDirs, string UserName);

public sealed class ForgeDeckBridge
{
    public const string Version = "0.1.0";

    private readonly ConfigStore _store;
    private readonly ToolScanner _scanner;
    private readonly TerminalSessionManager _terminal;
    private readonly LaunchService _launch = new();
    private readonly WorkdirHistoryService _workdirs;

    public BridgeDispatcher Dispatcher { get; }

    public ForgeDeckBridge(ConfigStore store, ToolScanner scanner, TerminalSessionManager terminal)
    {
        _store = store;
        _scanner = scanner;
        _terminal = terminal;
        _workdirs = new WorkdirHistoryService(store);
        Dispatcher = new BridgeDispatcher();
        RegisterMethods();
        _terminal.Output += (id, chunk) => Dispatcher.Emit("terminal.data", new { sessionId = id, chunk });
        _terminal.Exited += (id, code) => Dispatcher.Emit("terminal.exit", new { sessionId = id, exitCode = code });
        _terminal.Changed += () => Dispatcher.Emit("sessions.changed", new { });
    }

    private void RegisterMethods()
    {
        Dispatcher.Register("app.info", _ =>
            Task.FromResult<object?>(new AppInfo(Version, Environment.UserName, _store.Config.LastScanAt, _store.Config.LastUsed)));

        Dispatcher.Register("tools.list", _ => Task.FromResult<object?>(ToolsList()));

        Dispatcher.Register("tools.rescan", _ =>
        {
            var found = _scanner.Scan(new ScanContext(_store.Config.Settings.ExtraScanDirs));
            var manual = _store.Config.Tools.Where(t => t.Manual).ToList();
            _store.Config.Tools = manual.Concat(found).ToList();
            _store.Config.LastScanAt = DateTime.UtcNow;
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("tools.addManual", p =>
        {
            var name = p?.GetProperty("name").GetString() ?? "";
            var exePath = p?.GetProperty("exePath").GetString() ?? "";
            if (name.Trim().Length == 0) throw new BridgeException("validation", "工具名称不能为空");
            if (!File.Exists(exePath)) throw new BridgeException("validation", $"可执行文件不存在：{exePath}");
            _store.Config.Tools.Add(new ToolInfo
            {
                Name = name.Trim(),
                ExePath = Path.GetFullPath(exePath),
                Source = "手动添加",
                Manual = true,
            });
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("profiles.get", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var profile = _store.Config.Profiles.FirstOrDefault(x => x.ToolId == toolId)
                          ?? DefaultProfile(toolId);
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.save", p =>
        {
            var profile = p?.GetProperty("profile").Deserialize<LaunchProfile>(JsonOptions.Create(o => o.WriteIndented = false))
                ?? throw new BridgeException("validation", "无效的配置");
            _store.Config.Profiles.RemoveAll(x => x.Id == profile.Id || x.ToolId == profile.ToolId);
            _store.Config.Profiles.Add(profile);
            _store.Save();
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.delete", p =>
        {
            var id = p?.GetProperty("id").GetString() ?? "";
            _store.Config.Profiles.RemoveAll(x => x.Id == id);
            _store.Save();
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("settings.get", _ => Task.FromResult<object?>(new SettingsInfo(
            _store.Config.Settings, CommonDirs(), Environment.UserName)));

        Dispatcher.Register("settings.save", p =>
        {
            var settings = p?.GetProperty("settings").Deserialize<AppSettings>(JsonOptions.Create(o => o.WriteIndented = false))
                ?? throw new BridgeException("validation", "无效的设置");
            _store.Config.Settings = settings;
            _store.Save();
            return Task.FromResult<object?>(new SettingsInfo(settings, CommonDirs(), Environment.UserName));
        });

        Dispatcher.Register("workdirs.list", _ => Task.FromResult<object?>(_workdirs.List()));
        Dispatcher.Register("workdirs.add", p =>
        {
            _workdirs.Add(p?.GetProperty("path").GetString() ?? "");
            return Task.FromResult<object?>(_workdirs.List());
        });
        Dispatcher.Register("workdirs.remove", p =>
        {
            _workdirs.Remove(p?.GetProperty("path").GetString() ?? "");
            return Task.FromResult<object?>(_workdirs.List());
        });

        Dispatcher.Register("sessions.list", _ => Task.FromResult<object?>(_terminal.List()));

        Dispatcher.Register("terminal.create", async p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var tool = _store.Config.Tools.FirstOrDefault(t => t.Id == toolId)
                ?? throw new BridgeException("not_found", "工具不存在");
            var profile = ResolveProfile(toolId, p);
            _launch.Validate(tool, profile);
            var command = _launch.BuildCommand(tool, profile);
            var workdir = LaunchService.ResolveWorkdir(profile);
            var (cols, rows) = Size(p);
            var sessionId = await _terminal.CreateAsync(
                tool.Name, command.App, command.Args, workdir, _launch.ResolveEnv(profile), cols, rows);
            RecordUsage(tool, workdir);
            return new { sessionId };
        });

        Dispatcher.Register("terminal.createShell", async p =>
        {
            var (app, title) = _store.Config.Settings.DefaultShell switch
            {
                "pwsh" => (PathSearch.FindOnPath("pwsh")
                           ?? throw new BridgeException("not_found", "未找到 pwsh，请在设置中改用 powershell 或 cmd"), "pwsh"),
                // powershell 分支复用 LaunchService 三级回退的第二、三级：PATH 上的 powershell → System32 全路径
                "powershell" => (PathSearch.FindOnPath("powershell")
                                 ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powershell.exe"), "PowerShell"),
                _ => (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "cmd"),
            };
            var (cols, rows) = Size(p);
            var sessionId = await _terminal.CreateAsync(
                title, app!, Array.Empty<string>(),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), null, cols, rows);
            return new { sessionId };
        });

        Dispatcher.Register("terminal.write", p =>
        {
            try
            {
                _terminal.Write(p?.GetProperty("sessionId").GetString() ?? "", p?.GetProperty("data").GetString() ?? "");
            }
            catch (KeyNotFoundException)
            {
                // 关标签瞬间在途 write 是良性竞态：session-gone 供前端静默忽略，不弹错误
                throw new BridgeException("session-gone", "会话已关闭");
            }
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.resize", p =>
        {
            try
            {
                _terminal.Resize(p?.GetProperty("sessionId").GetString() ?? "",
                    p?.GetProperty("cols").GetInt32() ?? 80, p?.GetProperty("rows").GetInt32() ?? 24);
            }
            catch (KeyNotFoundException)
            {
                throw new BridgeException("session-gone", "会话已关闭");
            }
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.kill", p =>
        {
            _terminal.Kill(p?.GetProperty("sessionId").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("terminal.close", p =>
        {
            _terminal.Close(p?.GetProperty("sessionId").GetString() ?? "");
            return Task.FromResult<object?>(null);
        });

        Dispatcher.Register("launch.external", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var tool = _store.Config.Tools.FirstOrDefault(t => t.Id == toolId)
                ?? throw new BridgeException("not_found", "工具不存在");
            var profile = ResolveProfile(toolId, p);
            var pid = _launch.LaunchExternal(tool, profile);
            RecordUsage(tool, LaunchService.ResolveWorkdir(profile));
            return Task.FromResult<object?>(new { pid });
        });
    }

    private List<ToolListItem> ToolsList() =>
        _store.Config.Tools.Select(t => new ToolListItem(
            t,
            File.Exists(t.ExePath),
            _store.Config.Profiles.FirstOrDefault(p => p.ToolId == t.Id)?.OpenMode
                ?? (_store.Config.Settings.PreferEmbedded ? OpenMode.Embedded : OpenMode.External)))
        .ToList();

    private LaunchProfile ResolveProfile(string toolId, JsonElement? p)
    {
        if (p != null && p.Value.TryGetProperty("profileId", out var idEl) &&
            idEl.GetString() is { Length: > 0 } profileId)
        {
            var byId = _store.Config.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (byId != null) return byId;
        }
        return _store.Config.Profiles.FirstOrDefault(x => x.ToolId == toolId) ?? DefaultProfile(toolId);
    }

    private LaunchProfile DefaultProfile(string toolId) => new()
    {
        ToolId = toolId,
        OpenMode = _store.Config.Settings.PreferEmbedded ? OpenMode.Embedded : OpenMode.External,
    };

    private void RecordUsage(ToolInfo tool, string workdir)
    {
        _store.Config.LastUsed = new LastUsedInfo { ToolId = tool.Id, Workdir = workdir };
        _store.Save();
        if (Directory.Exists(workdir)) _workdirs.Add(workdir);
    }

    private static (int cols, int rows) Size(JsonElement? p)
    {
        var cols = p?.TryGetProperty("cols", out var c) == true && c.TryGetInt32(out var cv) ? cv : 120;
        var rows = p?.TryGetProperty("rows", out var r) == true && r.TryGetInt32(out var rv) ? rv : 30;
        return (cols, rows);
    }

    private static List<CommonDir> CommonDirs()
    {
        var dirs = new List<CommonDir>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Add(string name, string? path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                dirs.Add(new CommonDir(name, path));
        }
        Add("主目录", home);
        Add("桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add("文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Add("下载", Path.Combine(home, "Downloads"));
        try
        {
            foreach (var drive in Directory.GetLogicalDrives())
                dirs.Add(new CommonDir(drive, drive));
        }
        catch (IOException) { }
        return dirs;
    }
}
