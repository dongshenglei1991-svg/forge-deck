using ForgeDeck.Core.Terminal;

namespace ForgeDeck.Core.Tests;

public class TerminalSessionManagerTests : IDisposable
{
    private static readonly string CmdExe =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
    private readonly TerminalSessionManager _mgr = new();

    public void Dispose() => _mgr.Dispose();

    private static async Task<string> WaitForOutputAsync(
        TerminalSessionManager mgr, string sessionId, Func<string, bool> done, TimeSpan timeout)
    {
        var acc = "";
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOutput(string id, string chunk)
        {
            if (id != sessionId) return;
            acc += chunk;
            if (done(acc)) tcs.TrySetResult(acc);
        }
        mgr.Output += OnOutput;
        try
        {
            await Task.WhenAny(tcs.Task, Task.Delay(timeout));
            Assert.True(tcs.Task.IsCompleted, $"超时未收到预期输出，当前输出：{acc}");
            return acc;
        }
        finally { mgr.Output -= OnOutput; }
    }

    private static async Task<int> WaitForExitAsync(TerminalSessionManager mgr, string sessionId, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnExit(string id, int code) { if (id == sessionId) tcs.TrySetResult(code); }
        mgr.Exited += OnExit;
        // cmd /c 类短命进程：退出可能先于本方法订阅——订阅后先查快照补一次
        var snapshot = mgr.List().FirstOrDefault(s => s.SessionId == sessionId);
        if (snapshot is { Running: false }) tcs.TrySetResult(snapshot.ExitCode ?? -1);
        var winner = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        mgr.Exited -= OnExit;
        Assert.True(tcs.Task.IsCompleted, "超时未收到退出事件");
        return tcs.Task.Result;
    }

    [Fact]
    public async Task Create_CmdEcho_CapturesOutputAndExit()
    {
        var id = await _mgr.CreateAsync("echo", CmdExe, new[] { "/c", "echo forgedeck-ok" }, Path.GetTempPath());
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forgedeck-ok"), TimeSpan.FromSeconds(10));
        var code = await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
        Assert.Equal(0, code);
        var session = Assert.Single(_mgr.List());
        Assert.False(session.Running);
        Assert.Equal(0, session.ExitCode);
    }

    [Fact]
    public async Task Create_SpaceInArgument_SurvivesCommandLine()
    {
        // verbatim 模式 Porta 不加引号，引号由管理器的 QuoteIfSpaced 自行添加；
        // 含连续双空格的参数作单 token 到达时，cmd echo 原样回显带引号原文（"forge  deck"）。
        // 实测：cmd echo 不重 join、不折叠空格——引号丢失（参数裂开）时回显不含引号，
        // 故断言带引号 + 双空格才真正可捕捉 QuoteIfSpaced 被误删的回归。
        var id = await _mgr.CreateAsync("echo2", CmdExe,
            new[] { "/c", "echo", "forge  deck" }, Path.GetTempPath());
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("\"forge  deck\""), TimeSpan.FromSeconds(10));
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task EnvVars_ReachChildProcess()
    {
        var id = await _mgr.CreateAsync("env", CmdExe, new[] { "/c", "echo %FD_TEST_A%" }, Path.GetTempPath(),
            env: new Dictionary<string, string> { ["FD_TEST_A"] = "forge-env-ok" });
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forge-env-ok"), TimeSpan.FromSeconds(10));
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Write_InteractiveCmd_EchoesInput()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600); // 等 shell 就绪
        _mgr.Write(id, "echo forge-input-test\r");
        await WaitForOutputAsync(_mgr, id, acc => acc.Contains("forge-input-test"), TimeSpan.FromSeconds(10));
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Resize_DoesNotThrow()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        _mgr.Resize(id, 100, 30);
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Close_RemovesSessionFromList_AndKillsProcess()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        _mgr.Close(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
        Assert.Empty(_mgr.List());
    }

    [Fact]
    public async Task HasRunningSessions_ReflectsState()
    {
        var id = await _mgr.CreateAsync("shell", CmdExe, new[] { "/k" }, Path.GetTempPath());
        await Task.Delay(600);
        Assert.True(_mgr.HasRunningSessions);
        _mgr.Kill(id);
        await WaitForExitAsync(_mgr, id, TimeSpan.FromSeconds(10));
        Assert.False(_mgr.HasRunningSessions);
    }
}
