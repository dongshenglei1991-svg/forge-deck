using System.Text.Json;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Launching;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Bridge;

public sealed record ToolListItem(ToolInfo Tool, bool Exists, OpenMode DefaultMode);
public sealed record HiddenToolItem(string ExePath, string Name, string Source, string? ToolId);
public sealed record CommonDir(string Name, string Path);
public sealed record AppInfo(string Version, string UserName, DateTime? LastScanAt, LastUsedInfo? LastUsed);
public sealed record SettingsInfo(AppSettings Settings, IReadOnlyList<CommonDir> CommonDirs, string UserName);

/// <summary>业务方法接线。线程模型：handler 由宿主在 UI 线程串行调用，ConfigStore 变更无需加锁；
/// 唯一例外 tools.rescan——扫描与合并已 Task.Run 化，且只读脱离 store 的快照、不触碰 ConfigStore，
/// 合并结果回 UI 线程写回。终端 Output/Exited/Changed 事件来自后台线程，经 Dispatcher.Emit 透传
/// （Emit 仅做序列化，不写共享状态）。</summary>
public sealed class ForgeDeckBridge
{
    public const string Version = "0.1.0";

    // 反序列化复用同一 options 实例，保留 STJ 元数据缓存（WriteIndented 对反序列化无影响，可复用 Opts 风格）
    private static readonly JsonSerializerOptions PayloadOpts = JsonOptions.Create(o => o.WriteIndented = false);

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

        Dispatcher.Register("tools.rescan", async _ =>
        {
            // 快照脱离 store（后台不得触碰 ConfigStore）；扫描与合并在线程池执行——
            // UI 线程同步扫描会冻结窗口并反压终端输出泵；合并结果在 UI 线程写回
            var snapshot = _store.Config.Tools.Select(CloneTool).ToList();
            var extraDirs = _store.Config.Settings.ExtraScanDirs.ToList();
            var hidden = _store.Config.HiddenExePaths.ToList();
            var merged = await Task.Run(() =>
                MergeScanResults(snapshot, _scanner.Scan(new ScanContext(extraDirs)), hidden));
            _store.Config.Tools = merged;
            _store.Config.LastScanAt = DateTime.UtcNow;
            _store.Save();
            return ToolsList();
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
            return Task.FromResult<object?>(GetOrCreateProfile(toolId));
        });

        Dispatcher.Register("profiles.list", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            return Task.FromResult<object?>(ProfilesFor(toolId).ToList());
        });

        Dispatcher.Register("profiles.save", p =>
        {
            var profile = p?.GetProperty("profile").Deserialize<LaunchProfile>(PayloadOpts)
                ?? throw new BridgeException("validation", "无效的配置");
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            _store.Config.Profiles.RemoveAll(x => x.Id == profile.Id);
            _store.Config.Profiles.Add(profile);
            RememberLastProfile(profile.ToolId, profile.Id);
            _store.Save();
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.create", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            string? fromId = null;
            try { fromId = p?.GetProperty("fromProfileId").GetString(); } catch (KeyNotFoundException) { }
            LaunchProfile source;
            if (!string.IsNullOrEmpty(fromId))
            {
                source = _store.Config.Profiles.FirstOrDefault(x => x.Id == fromId)
                         ?? throw new BridgeException("not_found", "配置不存在");
            }
            else
            {
                source = DefaultProfile(toolId);
            }
            var created = new LaunchProfile
            {
                ToolId = toolId,
                Name = UniqueProfileName(toolId, string.IsNullOrEmpty(fromId) ? "默认" : "副本"),
                Args = source.Args,
                Env = new Dictionary<string, string>(source.Env),
                Workdir = source.Workdir,
                OpenMode = source.OpenMode,
                AutoRestore = source.AutoRestore,
            };
            _store.Config.Profiles.Add(created);
            RememberLastProfile(toolId, created.Id);
            _store.Save();
            return Task.FromResult<object?>(created);
        });

        Dispatcher.Register("profiles.rename", p =>
        {
            var id = p?.GetProperty("id").GetString() ?? "";
            var name = (p?.GetProperty("name").GetString() ?? "").Trim();
            if (name.Length == 0) throw new BridgeException("validation", "配置名称不能为空");
            var profile = _store.Config.Profiles.FirstOrDefault(x => x.Id == id)
                          ?? throw new BridgeException("not_found", "配置不存在");
            if (_store.Config.Profiles.Any(x =>
                    x.Id != id && x.ToolId == profile.ToolId &&
                    string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new BridgeException("validation", "该工具已有同名配置");
            profile.Name = name;
            _store.Save();
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("profiles.delete", p =>
        {
            var id = p?.GetProperty("id").GetString() ?? "";
            var profile = _store.Config.Profiles.FirstOrDefault(x => x.Id == id)
                          ?? throw new BridgeException("not_found", "配置不存在");
            var toolId = profile.ToolId;
            _store.Config.Profiles.RemoveAll(x => x.Id == id);
            var current = ProfilesFor(toolId).FirstOrDefault();
            if (current == null)
            {
                current = DefaultProfile(toolId);
                _store.Config.Profiles.Add(current);
            }
            RememberLastProfile(toolId, current.Id);
            _store.Save();
            return Task.FromResult<object?>(current);
        });

        Dispatcher.Register("profiles.select", p =>
        {
            var toolId = p?.GetProperty("toolId").GetString() ?? "";
            var profileId = p?.GetProperty("profileId").GetString() ?? "";
            var profile = _store.Config.Profiles.FirstOrDefault(x => x.Id == profileId && x.ToolId == toolId)
                          ?? throw new BridgeException("not_found", "配置不存在");
            RememberLastProfile(toolId, profile.Id);
            _store.Save();
            return Task.FromResult<object?>(profile);
        });

        Dispatcher.Register("tools.hide", p =>
        {
            var tool = RequireTool(p);
            if (tool.Manual) throw new BridgeException("validation", "手动添加的工具请删除，不能隐藏");
            var path = NormalizePath(tool.ExePath);
            if (!_store.Config.HiddenExePaths.Any(h =>
                    string.Equals(NormalizePath(h), path, StringComparison.OrdinalIgnoreCase)))
                _store.Config.HiddenExePaths.Add(path);
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("tools.unhide", p =>
        {
            var path = NormalizePath(p?.GetProperty("exePath").GetString() ?? "");
            _store.Config.HiddenExePaths.RemoveAll(h =>
                string.Equals(NormalizePath(h), path, StringComparison.OrdinalIgnoreCase));
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("tools.delete", p =>
        {
            var tool = RequireTool(p);
            if (!tool.Manual) throw new BridgeException("validation", "只能删除手动添加的工具");
            _store.Config.Tools.RemoveAll(t => t.Id == tool.Id);
            _store.Config.Profiles.RemoveAll(x => x.ToolId == tool.Id);
            _store.Config.LastProfileByTool.Remove(tool.Id);
            if (_store.Config.LastUsed?.ToolId == tool.Id) _store.Config.LastUsed = null;
            var path = NormalizePath(tool.ExePath);
            _store.Config.HiddenExePaths.RemoveAll(h =>
                string.Equals(NormalizePath(h), path, StringComparison.OrdinalIgnoreCase));
            _store.Save();
            return Task.FromResult<object?>(ToolsList());
        });

        Dispatcher.Register("tools.relocate", p =>
        {
            var tool = RequireTool(p);
            var exePath = p?.GetProperty("exePath").GetString() ?? "";
            if (!File.Exists(exePath)) throw new BridgeException("validation", $"可执行文件不存在：{exePath}");
            var full = Path.GetFullPath(exePath);
            if (_store.Config.Tools.Any(t =>
                    t.Id != tool.Id &&
                    string.Equals(NormalizePath(t.ExePath), full, StringComparison.OrdinalIgnoreCase)))
                throw new BridgeException("validation", "该路径已被其它工具占用");
            tool.ExePath = full;
            tool.PathPinned = true;
            _store.Save();
            return Task.FromResult<object?>(ToolsList().First(t => t.Tool.Id == tool.Id));
        });

        Dispatcher.Register("tools.hidden", _ =>
            Task.FromResult<object?>(_store.Config.HiddenExePaths.Select(h =>
            {
                var full = NormalizePath(h);
                var tool = _store.Config.Tools.FirstOrDefault(t =>
                    string.Equals(NormalizePath(t.ExePath), full, StringComparison.OrdinalIgnoreCase));
                return new HiddenToolItem(full, tool?.Name ?? full, tool?.Source ?? "", tool?.Id);
            }).ToList()));

        Dispatcher.Register("settings.get", _ => Task.FromResult<object?>(new SettingsInfo(
            _store.Config.Settings, CommonDirs(), Environment.UserName)));

        Dispatcher.Register("settings.save", p =>
        {
            var settings = p?.GetProperty("settings").Deserialize<AppSettings>(PayloadOpts)
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
            // 参数提取在 try 外：畸形请求（缺属性）应报 internal，不得误标 session-gone
            var sessionId = p?.GetProperty("sessionId").GetString() ?? "";
            var data = p?.GetProperty("data").GetString() ?? "";
            try
            {
                _terminal.Write(sessionId, data);
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
            var sessionId = p?.GetProperty("sessionId").GetString() ?? "";
            var (cols, rows) = Size(p);
            try
            {
                _terminal.Resize(sessionId, cols, rows);
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
        _store.Config.Tools.Where(t => !IsHidden(t.ExePath)).Select(t => new ToolListItem(
            t,
            File.Exists(t.ExePath),
            DefaultModeFor(t)))
        .ToList();

    private OpenMode DefaultModeFor(ToolInfo t)
    {
        if (_store.Config.LastProfileByTool.TryGetValue(t.Id, out var pid))
        {
            var last = _store.Config.Profiles.FirstOrDefault(p => p.Id == pid);
            if (last != null) return last.OpenMode;
        }
        return _store.Config.Profiles.FirstOrDefault(p => p.ToolId == t.Id)?.OpenMode
               ?? (_store.Config.Settings.PreferEmbedded ? OpenMode.Embedded : OpenMode.External);
    }

    /// <summary>重扫合并：按 ExePath（OrdinalIgnoreCase）复用旧条目，保留 Id 与 Manual 标记——
    /// 否则每次重扫重铸 Id，profile/lastUsed 会静默失联（autoScanOnStartup=true 时每次启动都被重置）。
    /// 复用条目刷新展示字段（Name/Type/Source/Builtin），新路径才铸造新条目；
    /// 手加、路径钉住、已隐藏条目一律保留；其余未扫到的自动识别条目淘汰。仅触碰快照，不改 ConfigStore。</summary>
    private static List<ToolInfo> MergeScanResults(
        List<ToolInfo> oldTools, List<ToolInfo> found, IReadOnlyList<string> hiddenExePaths)
    {
        var hidden = new HashSet<string>(hiddenExePaths.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        var oldByPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var old in oldTools)
            oldByPath[NormalizePath(old.ExePath)] = old;

        var result = new List<ToolInfo>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Keep(ToolInfo t)
        {
            if (added.Add(NormalizePath(t.ExePath))) result.Add(t);
        }

        foreach (var old in oldTools)
        {
            if (old.Manual || old.PathPinned || hidden.Contains(NormalizePath(old.ExePath)))
                Keep(old);
        }

        foreach (var fresh in found)
        {
            var path = NormalizePath(fresh.ExePath);
            if (hidden.Contains(path))
            {
                if (oldByPath.TryGetValue(path, out var hiddenExisting))
                    CopyDisplay(hiddenExisting, fresh);
                continue;
            }

            if (oldByPath.TryGetValue(path, out var existing))
            {
                if (!existing.PathPinned) CopyDisplay(existing, fresh);
                Keep(existing);
            }
            else
            {
                Keep(fresh);
            }
        }
        return result;
    }

    private static void CopyDisplay(ToolInfo target, ToolInfo source)
    {
        target.Name = source.Name;
        target.Type = source.Type;
        target.Source = source.Source;
        target.Builtin = source.Builtin;
    }

    private static ToolInfo CloneTool(ToolInfo t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Type = t.Type,
        ExePath = t.ExePath,
        Source = t.Source,
        Builtin = t.Builtin,
        Manual = t.Manual,
        PathPinned = t.PathPinned,
    };

    private ToolInfo RequireTool(JsonElement? p)
    {
        var toolId = p?.GetProperty("toolId").GetString() ?? "";
        return _store.Config.Tools.FirstOrDefault(t => t.Id == toolId)
               ?? throw new BridgeException("not_found", "工具不存在");
    }

    private LaunchProfile GetOrCreateProfile(string toolId)
    {
        if (_store.Config.LastProfileByTool.TryGetValue(toolId, out var lastId))
        {
            var last = _store.Config.Profiles.FirstOrDefault(x => x.Id == lastId && x.ToolId == toolId);
            if (last != null) return last;
        }
        var first = ProfilesFor(toolId).FirstOrDefault();
        if (first != null)
        {
            RememberLastProfile(toolId, first.Id);
            _store.Save();
            return first;
        }
        var fresh = DefaultProfile(toolId);
        _store.Config.Profiles.Add(fresh);
        RememberLastProfile(toolId, fresh.Id);
        _store.Save();
        return fresh;
    }

    private IEnumerable<LaunchProfile> ProfilesFor(string toolId) =>
        _store.Config.Profiles
            .Where(p => p.ToolId == toolId)
            .OrderBy(p => p.Name.Equals("默认", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase);

    private string UniqueProfileName(string toolId, string stem)
    {
        var names = _store.Config.Profiles.Where(p => p.ToolId == toolId)
            .Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(stem)) return stem;
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} {i}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private void RememberLastProfile(string toolId, string profileId) =>
        _store.Config.LastProfileByTool[toolId] = profileId;

    private bool IsHidden(string exePath) =>
        _store.Config.HiddenExePaths.Any(h =>
            string.Equals(NormalizePath(h), NormalizePath(exePath), StringComparison.OrdinalIgnoreCase));

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (ArgumentException) { return path; }
        catch (NotSupportedException) { return path; }
        catch (PathTooLongException) { return path; }
    }

    private LaunchProfile ResolveProfile(string toolId, JsonElement? p)
    {
        if (p != null && p.Value.TryGetProperty("profileId", out var idEl) &&
            idEl.GetString() is { Length: > 0 } profileId)
        {
            var byId = _store.Config.Profiles.FirstOrDefault(x => x.Id == profileId);
            if (byId != null) return byId;
        }
        return GetOrCreateProfile(toolId);
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
