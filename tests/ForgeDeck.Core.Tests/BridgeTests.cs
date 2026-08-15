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
    private readonly ForgeDeckBridge _bridge = null!;

    public BridgeTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ConfigStore(Path.Combine(_dir, "config.json"));
        _store.Load();
        _bridge = new ForgeDeckBridge(
            _store,
            new ToolScanner(new IScanSource[] { new EmptySource() }),
            _terminal);
    }

    public void Dispose()
    {
        _terminal.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class EmptySource : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) { yield break; }
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
        var resp = await _bridge.Dispatcher.HandleAsync(
            """{"id":8,"method":"terminal.create","params":{"toolId":"tc1","cols":80,"rows":24}}""");
        var sessionId = ResultOf(resp!).GetProperty("sessionId").GetString();
        Assert.NotNull(sessionId);

        var listResp = await _bridge.Dispatcher.HandleAsync("""{"id":9,"method":"sessions.list"}""");
        Assert.Equal(1, ResultOf(listResp!).GetArrayLength());
        // lastUsed 与工作目录历史联动
        Assert.NotNull(_store.Config.LastUsed);
        Assert.Equal("tc1", _store.Config.LastUsed!.ToolId);
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
