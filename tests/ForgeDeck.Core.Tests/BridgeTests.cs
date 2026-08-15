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
    public async Task Workdirs_AddAndList()
    {
        await _bridge.Dispatcher.HandleAsync(
            $$$"""{"id":14,"method":"workdirs.add","params":{"path":"{{{_dir.Replace("\\", "\\\\")}}}"}}""");
        var resp = await _bridge.Dispatcher.HandleAsync("""{"id":15,"method":"workdirs.list"}""");
        Assert.Equal(_dir, ResultOf(resp!)[0].GetString());
    }
}
