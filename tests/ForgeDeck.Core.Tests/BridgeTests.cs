using System.Text.Json;
using ForgeDeck.Core;
using ForgeDeck.Core.Bridge;
using ForgeDeck.Core.Config;
using ForgeDeck.Core.Scanning;
using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Tests;

public class BridgeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private readonly ConfigStore _store = null!;
    // TerminalCreate_WithCmdTool 会 spawn 真实进程：终端必须存字段并在 Dispose 释放
    private readonly TerminalSessionManager _terminal = new();
    // 可注入命中：rescan 复用测试需要扫描器返回既有工具同路径的命中
    private readonly List<ScanHit> _scanHits = new();
    private readonly ForgeDeckBridge _bridge = null!;

    public BridgeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[] { new FixedSource(_scanHits) }),
            _terminal);
    }

    public void Dispose()
    {
        _terminal.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FixedSource(List<ScanHit> hits) : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) => hits;
    }

    private static JsonElement ResultOf(string response)
    {
        // Clone 脱离文档生命周期：using 释放后返回的 JsonElement 仍可安全访问
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.GetProperty("result").Clone();
    }

    private static (string Code, string Message)? ErrorOf(string response)
    {
        using var doc = JsonDocument.Parse(response);
        if (!doc.RootElement.TryGetProperty("error", out var err)) return null;
        return (err.GetProperty("code").GetString()!, err.GetProperty("message").GetString()!);
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("{ broken");
        Assert.NotNull(resp);
        Assert.Equal(("-32700", "请求不是合法 JSON"), ErrorOf(resp!));
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":1,"method":"nope.nope"}""");
        Assert.NotNull(resp);
        var (code, _) = ErrorOf(resp!)!.Value;
        Assert.Equal("-32601", code);
    }

    [Fact]
    public async Task HandleAsync_NullBodyOrNonStringMethod_ReturnsErrorNotThrow()
    {
        // 宿主 TryGetWebMessageAsString 可能返回 null；method 为数字时 GetString 会抛——都不得让异常逃逸
        var nullResp = await _bridge.Dispatcher.HandleAsync(null!);
        Assert.NotNull(nullResp);
        Assert.Equal("-32700", ErrorOf(nullResp!)!.Value.Code);

        var numMethodResp = await _bridge.Dispatcher.HandleAsync("""{"id":40,"method":123}""");
        Assert.NotNull(numMethodResp);
        Assert.Equal("-32602", ErrorOf(numMethodResp!)!.Value.Code);
    }

    [Fact]
    public async Task AppInfo_ReturnsVersionAndUser()
    {
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":2,"method":"app.info"}""");
        var result = ResultOf(resp!);
        Assert.Equal("0.1.0", result.GetProperty("version").GetString());
        Assert.Equal(Environment.UserName, result.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task AddManual_InvalidPath_ReturnsValidationError()
    {
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":3,"method":"tools.addManual","params":{"name":"X","exePath":"{{{Path.Combine(_dir, "ghost.exe").Replace("\\", "\\\\")}}}"}}""");
        var (code, message) = ErrorOf(resp!)!.Value;
        Assert.Equal("validation", code);
        Assert.Contains("可执行文件不存在", message);
    }

    [Fact]
    public async Task AddManual_Valid_AddsToolAndPersists()
    {
        var exe = Path.Combine(_dir, "mytool.exe");
        File.WriteAllText(exe, "");
        var exeJson = exe.Replace("\\", "\\\\");
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":4,"method":"tools.addManual","params":{"name":"MyTool","exePath":"{{{exeJson}}}"}}""");
        var result = ResultOf(resp!);
        Assert.Equal(1, result.GetArrayLength());
        Assert.Equal("MyTool", result[0].GetProperty("tool").GetProperty("name").GetString());

        var reloaded = new ConfigStore(Path.Combine(_dir, "config.json"));
        reloaded.Load();
        Assert.Contains(reloaded.Config.Tools, t => t.Name == "MyTool" && t.Manual);
    }

    [Fact]
    public async Task ProfilesGet_Missing_ReturnsDefaultWithPreferEmbedded()
    {
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":5,"method":"profiles.get","params":{"toolId":"t1"}}""");
        var result = ResultOf(resp!);
        Assert.Equal("embedded", result.GetProperty("openMode").GetString());
        Assert.Equal("t1", result.GetProperty("toolId").GetString());
    }

    [Fact]
    public async Task ProfilesSave_ThenGet_ReturnsSaved()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":6,"method":"profiles.save","params":{"profile":{"id":"p1","toolId":"t1","name":"默认","args":"--x","env":{"K":"V"},"workdir":"","openMode":"external","autoRestore":false}}}""");
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":7,"method":"profiles.get","params":{"toolId":"t1"}}""");
        var result = ResultOf(resp!);
        Assert.Equal("p1", result.GetProperty("id").GetString());
        Assert.Equal("external", result.GetProperty("openMode").GetString());
    }

    [Fact]
    public async Task TerminalCreate_WithCmdTool_CreatesSession()
    {
        var cmdScript = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(cmdScript, "@echo off\r\necho forge-bridge-e2e\r\n");
        _store.Config.Tools.Add(new ToolInfo { Id = "tc1", Name = "Fake Claude", ExePath = cmdScript, Source = "测试" });

        // 事件转发：Output → terminal.data 事件封包（订阅须先于创建，首块输出可能立即可达）
        var outgoing = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var acc = "";
        void OnOutgoing(string message)
        {
            try
            {
                using var doc = JsonDocument.Parse(message);
                if (doc.RootElement.TryGetProperty("event", out var ev) && ev.GetString() == "terminal.data")
                {
                    acc += doc.RootElement.GetProperty("data").GetProperty("chunk").GetString() ?? "";
                    if (acc.Contains("forge-bridge-e2e")) outgoing.TrySetResult(message);
                }
            }
            catch (JsonException) { }
        }
        _bridge.Dispatcher.Outgoing += OnOutgoing;
        try
        {
            var resp = await _bridge.Dispatcher.HandleAsync(
                """{"id":8,"method":"terminal.create","params":{"toolId":"tc1","cols":80,"rows":24}}""");
            var sessionId = ResultOf(resp!).GetProperty("sessionId").GetString();
            Assert.NotNull(sessionId);

            var done = await Task.WhenAny(outgoing.Task, Task.Delay(TimeSpan.FromSeconds(2)));
            Assert.True(done == outgoing.Task, $"超时未收到 terminal.data 事件，累计输出：{acc}");
            var message = await outgoing.Task; // 上面的断言已保证完成
            using var payload = JsonDocument.Parse(message);
            Assert.Equal("terminal.data", payload.RootElement.GetProperty("event").GetString());
            Assert.Equal(sessionId, payload.RootElement.GetProperty("data").GetProperty("sessionId").GetString());

            var listResp = await _bridge.Dispatcher.HandleAsync("""{"id":9,"method":"sessions.list"}""");
            Assert.Equal(1, ResultOf(listResp!).GetArrayLength());
            // lastUsed 与工作目录历史联动
            Assert.NotNull(_store.Config.LastUsed);
            Assert.Equal("tc1", _store.Config.LastUsed!.ToolId);
        }
        finally { _bridge.Dispatcher.Outgoing -= OnOutgoing; }
    }

    [Fact]
    public async Task Rescan_ReusesToolByPath_PreservesIdProfileAndLastUsed()
    {
        var cmdScript = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(cmdScript, "@echo off\r\n");
        _store.Config.Tools.Add(new ToolInfo { Id = "keep1", Name = "Fake Claude", ExePath = cmdScript, Source = "测试" });
        await _bridge.Dispatcher.HandleAsync(
            """{"id":20,"method":"profiles.save","params":{"profile":{"id":"p20","toolId":"keep1","name":"默认","args":"","env":{},"workdir":"","openMode":"external","autoRestore":false}}}""");
        _store.Config.LastUsed = new LastUsedInfo { ToolId = "keep1", Workdir = _dir };

        // 同路径重扫：复用旧条目（Id 不变、展示字段刷新），profile/lastUsed 不失联
        _scanHits.Add(new ScanHit(cmdScript, null, "新扫描源"));
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":21,"method":"tools.rescan"}""");
        var list = ResultOf(resp!);
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("keep1", list[0].GetProperty("tool").GetProperty("id").GetString());
        Assert.Equal("新扫描源", list[0].GetProperty("tool").GetProperty("source").GetString());

        var profileResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":22,"method":"profiles.get","params":{"toolId":"keep1"}}""");
        Assert.Equal("p20", ResultOf(profileResp!).GetProperty("id").GetString());

        Assert.Contains(_store.Config.Tools, t => t.Id == _store.Config.LastUsed!.ToolId);
    }

    [Fact]
    public async Task LaunchExternal_CmdExitZero_ReturnsPidAndRecordsUsage()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        _store.Config.Tools.Add(new ToolInfo { Id = "le1", Name = "cmd", ExePath = cmdExe, Source = "测试" });
        await _bridge.Dispatcher.HandleAsync(
            """{"id":31,"method":"profiles.save","params":{"profile":{"id":"p31","toolId":"le1","name":"默认","args":"/c exit 0","env":{},"workdir":"","openMode":"external","autoRestore":false}}}""");
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":32,"method":"launch.external","params":{"toolId":"le1"}}""");
        Assert.True(ResultOf(resp!).GetProperty("pid").GetInt32() > 0);
        Assert.NotNull(_store.Config.LastUsed);
        Assert.Equal("le1", _store.Config.LastUsed!.ToolId);
    }

    [Fact]
    public async Task TerminalWrite_UnknownSession_ReturnsSessionGone()
    {
        // 关标签瞬间在途 write/resize 是良性竞态：统一映射为 session-gone，前端静默忽略
        var writeResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":10,"method":"terminal.write","params":{"sessionId":"nope","data":"x"}}""");
        var (writeCode, _) = ErrorOf(writeResp!)!.Value;
        Assert.Equal("session-gone", writeCode);

        var resizeResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":11,"method":"terminal.resize","params":{"sessionId":"nope","cols":80,"rows":24}}""");
        var (resizeCode, _) = ErrorOf(resizeResp!)!.Value;
        Assert.Equal("session-gone", resizeCode);
    }

    [Fact]
    public async Task SettingsGetSave_RoundTrip()
    {
        var getResp = await _bridge.Dispatcher.HandleAsync("""{"id":12,"method":"settings.get"}""");
        var result = ResultOf(getResp!);
        Assert.True(result.GetProperty("commonDirs").GetArrayLength() > 0);

        var saveResp = await _bridge.Dispatcher.HandleAsync(
            """{"id":13,"method":"settings.save","params":{"settings":{"defaultShell":"cmd","autoScanOnStartup":false,"extraScanDirs":["D:\\Tools"],"skipExitConfirm":true,"preferEmbedded":false,"maxWorkdirHistory":20}}}""");
        Assert.Equal("cmd", ResultOf(saveResp!).GetProperty("settings").GetProperty("defaultShell").GetString());
        Assert.False(_store.Config.Settings.AutoScanOnStartup);
        Assert.True(_store.Config.Settings.SkipExitConfirm);
    }

    [Fact]
    public async Task ProfilesSave_SameTool_KeepsOtherProfiles()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":40,"method":"profiles.save","params":{"profile":{"id":"p-a","toolId":"t-multi","name":"默认","args":"--a","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        await _bridge.Dispatcher.HandleAsync(
            """{"id":41,"method":"profiles.save","params":{"profile":{"id":"p-b","toolId":"t-multi","name":"公司","args":"--b","env":{},"workdir":"D:\\work","openMode":"external","autoRestore":true}}}""");

        var list = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":42,"method":"profiles.list","params":{"toolId":"t-multi"}}"""));
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("默认", list[0].GetProperty("name").GetString());
        Assert.Equal("公司", list[1].GetProperty("name").GetString());
        Assert.Equal("p-b", _store.Config.LastProfileByTool["t-multi"]);
    }

    [Fact]
    public async Task ProfilesGet_ReturnsLastSelected()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":43,"method":"profiles.save","params":{"profile":{"id":"p-a","toolId":"t-sel","name":"默认","args":"","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        await _bridge.Dispatcher.HandleAsync(
            """{"id":44,"method":"profiles.save","params":{"profile":{"id":"p-b","toolId":"t-sel","name":"个人","args":"","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        await _bridge.Dispatcher.HandleAsync(
            """{"id":45,"method":"profiles.select","params":{"toolId":"t-sel","profileId":"p-a"}}""");

        var got = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":46,"method":"profiles.get","params":{"toolId":"t-sel"}}"""));
        Assert.Equal("p-a", got.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ProfilesCreate_CopiesFromSource_AndSelectsNew()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":47,"method":"profiles.save","params":{"profile":{"id":"p-src","toolId":"t-copy","name":"默认","args":"--resume","env":{"K":"V"},"workdir":"D:\\p","openMode":"external","autoRestore":true}}}""");
        var created = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":48,"method":"profiles.create","params":{"toolId":"t-copy","fromProfileId":"p-src"}}"""));
        Assert.Equal("副本", created.GetProperty("name").GetString());
        Assert.Equal("--resume", created.GetProperty("args").GetString());
        Assert.Equal("V", created.GetProperty("env").GetProperty("K").GetString());
        Assert.NotEqual("p-src", created.GetProperty("id").GetString());
        Assert.Equal(created.GetProperty("id").GetString(), _store.Config.LastProfileByTool["t-copy"]);
    }

    [Fact]
    public async Task ProfilesRename_RejectsDuplicateName()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":49,"method":"profiles.save","params":{"profile":{"id":"p1","toolId":"t-rn","name":"默认","args":"","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        await _bridge.Dispatcher.HandleAsync(
            """{"id":50,"method":"profiles.save","params":{"profile":{"id":"p2","toolId":"t-rn","name":"公司","args":"","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":51,"method":"profiles.rename","params":{"id":"p2","name":"默认"}}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
    }

    [Fact]
    public async Task ProfilesDelete_LastOne_RecreatesDefault()
    {
        await _bridge.Dispatcher.HandleAsync(
            """{"id":52,"method":"profiles.save","params":{"profile":{"id":"p-only","toolId":"t-last","name":"旧","args":"--x","env":{},"workdir":"","openMode":"external","autoRestore":false}}}""");
        var resp = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":53,"method":"profiles.delete","params":{"id":"p-only"}}"""));
        Assert.Equal("默认", resp.GetProperty("name").GetString());
        Assert.Equal("", resp.GetProperty("args").GetString());
        Assert.Single(_store.Config.Profiles, p => p.ToolId == "t-last");
    }

    [Fact]
    public async Task ToolsHide_FiltersList_AndRescanDoesNotBringBack()
    {
        var exe = Path.Combine(_dir, "scan-me.exe");
        File.WriteAllText(exe, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "hid1", Name = "ScanMe", ExePath = exe, Manual = false });
        _scanHits.Add(new ScanHit(exe, null, "PATH"));

        var hideResp = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":54,"method":"tools.hide","params":{"toolId":"hid1"}}"""));
        Assert.Equal(0, hideResp.GetArrayLength());

        var hidden = ResultOf(await _bridge.Dispatcher.HandleAsync("""{"id":55,"method":"tools.hidden"}"""));
        Assert.Equal(1, hidden.GetArrayLength());
        Assert.Equal("ScanMe", hidden[0].GetProperty("name").GetString());

        var rescan = ResultOf(await _bridge.Dispatcher.HandleAsync("""{"id":56,"method":"tools.rescan"}"""));
        Assert.Equal(0, rescan.GetArrayLength());
        Assert.Contains(_store.Config.Tools, t => t.Id == "hid1");
    }

    [Fact]
    public async Task ToolsUnhide_ReturnsToVisibleList()
    {
        var exe = Path.Combine(_dir, "back.exe");
        File.WriteAllText(exe, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "uh1", Name = "Back", ExePath = exe, Manual = false });
        await _bridge.Dispatcher.HandleAsync("""{"id":57,"method":"tools.hide","params":{"toolId":"uh1"}}""");

        var exeJson = exe.Replace("\\", "\\\\");
        var list = ResultOf(await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":58,"method":"tools.unhide","params":{"exePath":"{{{exeJson}}}"}}"""));
        Assert.Equal(1, list.GetArrayLength());
        Assert.Equal("uh1", list[0].GetProperty("tool").GetProperty("id").GetString());
    }

    [Fact]
    public async Task ToolsHide_Manual_ReturnsValidation()
    {
        var exe = Path.Combine(_dir, "hand.exe");
        File.WriteAllText(exe, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "man1", Name = "Hand", ExePath = exe, Manual = true });
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":59,"method":"tools.hide","params":{"toolId":"man1"}}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
    }

    [Fact]
    public async Task ToolsDelete_Manual_RemovesProfilesAndLastUsed()
    {
        var exe = Path.Combine(_dir, "del-me.exe");
        File.WriteAllText(exe, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "del1", Name = "Del", ExePath = exe, Manual = true });
        await _bridge.Dispatcher.HandleAsync(
            """{"id":60,"method":"profiles.save","params":{"profile":{"id":"p-del","toolId":"del1","name":"默认","args":"","env":{},"workdir":"","openMode":"embedded","autoRestore":false}}}""");
        _store.Config.LastUsed = new LastUsedInfo { ToolId = "del1", Workdir = _dir };

        var list = ResultOf(await _bridge.Dispatcher.HandleAsync(
            """{"id":61,"method":"tools.delete","params":{"toolId":"del1"}}"""));
        Assert.Equal(0, list.GetArrayLength());
        Assert.DoesNotContain(_store.Config.Profiles, p => p.ToolId == "del1");
        Assert.Null(_store.Config.LastUsed);
    }

    [Fact]
    public async Task ToolsDelete_Scanned_ReturnsValidation()
    {
        var exe = Path.Combine(_dir, "scan-del.exe");
        File.WriteAllText(exe, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "sc1", Name = "Scan", ExePath = exe, Manual = false });
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":62,"method":"tools.delete","params":{"toolId":"sc1"}}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
    }

    [Fact]
    public async Task ToolsRelocate_PinsPath_RescanDoesNotOverwrite()
    {
        var missing = Path.Combine(_dir, "gone.exe");
        var next = Path.Combine(_dir, "here.exe");
        File.WriteAllText(next, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "rel1", Name = "Moved", ExePath = missing, Manual = false });

        var nextJson = next.Replace("\\", "\\\\");
        var relocated = ResultOf(await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":63,"method":"tools.relocate","params":{"toolId":"rel1","exePath":"{{{nextJson}}}"}}"""));
        Assert.Equal(Path.GetFullPath(next), relocated.GetProperty("tool").GetProperty("exePath").GetString());
        Assert.True(relocated.GetProperty("tool").GetProperty("pathPinned").GetBoolean());

        var other = Path.Combine(_dir, "other-scan.exe");
        File.WriteAllText(other, "");
        _scanHits.Add(new ScanHit(other, null, "PATH"));
        var rescan = ResultOf(await _bridge.Dispatcher.HandleAsync("""{"id":64,"method":"tools.rescan"}"""));
        var pinned = Enumerable.Range(0, rescan.GetArrayLength())
            .Select(i => rescan[i].GetProperty("tool"))
            .First(t => t.GetProperty("id").GetString() == "rel1");
        Assert.Equal(Path.GetFullPath(next), pinned.GetProperty("exePath").GetString());
        Assert.True(pinned.GetProperty("pathPinned").GetBoolean());
    }

    [Fact]
    public async Task ToolsRelocate_PathTaken_ReturnsValidation()
    {
        var a = Path.Combine(_dir, "a.exe");
        var b = Path.Combine(_dir, "b.exe");
        File.WriteAllText(a, "");
        File.WriteAllText(b, "");
        _store.Config.Tools.Add(new ToolInfo { Id = "ta", Name = "A", ExePath = a });
        _store.Config.Tools.Add(new ToolInfo { Id = "tb", Name = "B", ExePath = b, Manual = false });
        var aJson = a.Replace("\\", "\\\\");
        var resp = await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":65,"method":"tools.relocate","params":{"toolId":"tb","exePath":"{{{aJson}}}"}}""");
        Assert.Equal("validation", ErrorOf(resp!)!.Value.Code);
        Assert.Equal(b, _store.Config.Tools.First(t => t.Id == "tb").ExePath);
    }

    [Fact]
    public async Task Workdirs_AddAndList()
    {
        await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":14,"method":"workdirs.add","params":{"path":"{{{_dir.Replace("\\", "\\\\")}}}"}}""");
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":15,"method":"workdirs.list"}""");
        Assert.Equal(_dir, ResultOf(resp!)[0].GetString());
    }
}
