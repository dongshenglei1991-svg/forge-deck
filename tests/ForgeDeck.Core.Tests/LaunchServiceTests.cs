using System.Diagnostics;
using System.Runtime.InteropServices;
using ForgeDeck.Core;
using ForgeDeck.Core.Launching;

namespace ForgeDeck.Core.Tests;

public class LaunchServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    private readonly LaunchService _service = new();

    public LaunchServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static ToolInfo Tool(string exe) => new() { Name = "T", ExePath = exe };
    private static LaunchProfile Profile(string args = "", string workdir = "", bool autoRestore = false) =>
        new() { ToolId = "t", Args = args, Workdir = workdir, AutoRestore = autoRestore };

    [Theory]
    [InlineData(@"--model ""sonnet 4"" --x", new[] { "--model", "sonnet 4", "--x" })]
    [InlineData("", new string[0])]
    [InlineData("  --a   --b  ", new[] { "--a", "--b" })]
    [InlineData("'quoted arg'", new[] { "quoted arg" })]
    [InlineData(@"--model ""unclosed", new[] { "--model", "unclosed" })]
    [InlineData(@"/x """" /y", new[] { "/x", "", "/y" })]
    public void SplitArgs_HandlesQuotesAndWhitespace(string input, string[] expected)
    {
        Assert.Equal(expected, LaunchService.SplitArgs(input));
    }

    [Fact]
    public void BuildCommand_Exe_RunsDirectly()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        var cmd = _service.BuildCommand(Tool(exe), Profile("--verbose"));
        Assert.Equal(exe, cmd.App);
        Assert.Equal(new[] { "--verbose" }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_CmdScript_WrapsWithCmdC()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var cmd = _service.BuildCommand(Tool(script), Profile("--model x"));
        Assert.EndsWith("cmd.exe", cmd.App);
        Assert.Equal(new[] { "/c", script, "--model", "x" }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_Ps1_WrapsWithPwshOrPowershell()
    {
        var script = Path.Combine(_dir, "tool.ps1");
        File.WriteAllText(script, "");
        var cmd = _service.BuildCommand(Tool(script), Profile());
        Assert.True(cmd.App.Contains("pwsh") || cmd.App.Contains("powershell"), $"实际 App: {cmd.App}");
        Assert.Equal(new[] { "-File", script }, cmd.Args);
    }

    [Fact]
    public void BuildCommand_ClaudeAutoRestore_AppendsResumeArgs()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var withRestore = _service.BuildCommand(Tool(script), Profile("--model x", autoRestore: true));
        Assert.Contains("--continue", withRestore.Args);
        var alreadyHas = _service.BuildCommand(Tool(script), Profile("--continue", autoRestore: true));
        Assert.Single(alreadyHas.Args.Where(a => a == "--continue"));
    }

    [Fact]
    public void BuildCommand_GrokAutoRestore_AppendsResumeArgs()
    {
        var exe = Path.Combine(_dir, "grok.exe");
        File.WriteAllText(exe, "");
        var withRestore = _service.BuildCommand(Tool(exe), Profile(autoRestore: true));
        Assert.Contains("--continue", withRestore.Args);
        var alreadyHas = _service.BuildCommand(Tool(exe), Profile("--continue", autoRestore: true));
        Assert.Single(alreadyHas.Args.Where(a => a == "--continue"));
    }

    [Theory]
    [InlineData("opencode.cmd", "--continue")]
    [InlineData("copilot.exe", "--continue")]
    [InlineData("qwen.cmd", "--continue")]
    [InlineData("cn.exe", "--resume")]
    [InlineData("gemini.cmd", "--resume")]
    public void BuildCommand_KnownCliAutoRestore_AppendsResumeArgs(string fileName, string resume)
    {
        var exe = Path.Combine(_dir, fileName);
        File.WriteAllText(exe, "");
        var withRestore = _service.BuildCommand(Tool(exe), Profile(autoRestore: true));
        Assert.Contains(resume, withRestore.Args);
        var alreadyHas = _service.BuildCommand(Tool(exe), Profile(resume, autoRestore: true));
        Assert.Single(alreadyHas.Args.Where(a => a == resume));
    }

    [Fact]
    public void BuildCommand_UnsupportedExtension_Throws()
    {
        var py = Path.Combine(_dir, "tool.py");
        File.WriteAllText(py, "");
        Assert.Throws<NotSupportedException>(() => _service.BuildCommand(Tool(py), Profile()));
    }

    [Fact]
    public void Validate_MissingExe_ThrowsWithMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _service.Validate(Tool(Path.Combine(_dir, "ghost.exe")), Profile(workdir: _dir)));
        Assert.Contains("可执行文件不存在", ex.Message);
    }

    [Fact]
    public void Validate_MissingWorkdir_Throws()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        Assert.Throws<InvalidOperationException>(
            () => _service.Validate(Tool(exe), Profile(workdir: Path.Combine(_dir, "nope"))));
    }

    [Fact]
    public void Validate_EmptyWorkdir_FallsBackToHome()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        _service.Validate(Tool(exe), Profile());
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            LaunchService.ResolveWorkdir(Profile()));
    }

    [Fact]
    public void ResolveEnv_ExpandsVariables_AndSkipsEmptyKeys()
    {
        try
        {
            Environment.SetEnvironmentVariable("FD_TEST_VAR", "hello");
            var profile = Profile();
            profile.Env["A"] = "%FD_TEST_VAR% world";
            profile.Env[" "] = "skip";
            var env = _service.ResolveEnv(profile);
            Assert.Equal("hello world", env["A"]);
            Assert.False(env.ContainsKey(" "));
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_VAR", null); }
    }

    [Fact]
    public void BuildExternalStartInfo_UsesRawArgsAndEnv()
    {
        var exe = Path.Combine(_dir, "tool.exe");
        File.WriteAllText(exe, "");
        var profile = Profile("--model \"sonnet 4\"", _dir);
        profile.Env["K"] = "V";
        var psi = _service.BuildExternalStartInfo(Tool(exe), profile);
        Assert.Equal(exe, psi.FileName);
        // 外部启动：分词重组，含空白的参数重新引用（"sonnet 4" 不裂成两个参数）
        Assert.Equal("--model \"sonnet 4\"", psi.Arguments);
        Assert.Equal(_dir, psi.WorkingDirectory);
        Assert.Equal("V", psi.EnvironmentVariables["K"]);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void BuildExternalStartInfo_Ps1_WrapsWithPowerShellHost()
    {
        var script = Path.Combine(_dir, "tool.ps1");
        File.WriteAllText(script, "");
        var psi = _service.BuildExternalStartInfo(Tool(script), Profile("-Flag x", _dir));
        Assert.True(psi.FileName.Contains("pwsh") || psi.FileName.Contains("powershell"), $"实际 FileName: {psi.FileName}");
        Assert.Equal($"-File \"{script}\" -Flag x", psi.Arguments);
    }

    [Fact]
    public void BuildExternalStartInfo_ClaudeAutoRestore_AppendsResumeArgs()
    {
        var script = Path.Combine(_dir, "claude.cmd");
        File.WriteAllText(script, "");
        var psi = _service.BuildExternalStartInfo(Tool(script), Profile("--model x", _dir, autoRestore: true));
        Assert.Equal("--model x --continue", psi.Arguments);
    }

    [Fact]
    public void LaunchExternal_CmdExitsWithCode()
    {
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var pid = _service.LaunchExternal(Tool(cmdPath), Profile("/c exit 3", _dir));
        Assert.Equal(3, WaitForExitCode(pid, 5000));
    }

    [Fact]
    public void LaunchExternal_Ps1Script_ExitsWithCode()
    {
        var script = Path.Combine(_dir, "exit5.ps1");
        File.WriteAllText(script, "exit 5");
        var pid = _service.LaunchExternal(Tool(script), Profile(workdir: _dir));
        Assert.Equal(5, WaitForExitCode(pid, 15000));
    }

    /// <summary>
    /// GetProcessById 得到的 Process 组件在 .NET 上不填充 ExitCode
    /// （抛 "Process was not started by this object"），故经句柄 P/Invoke 取退出码；
    /// 句柄须在等待退出之前取得（进程退出后 Handle 会重新 OpenProcess 并失败）。
    /// </summary>
    private static int WaitForExitCode(int pid, int timeoutMs)
    {
        using var process = Process.GetProcessById(pid);
        var handle = process.Handle;
        if (!process.WaitForExit(timeoutMs))
            throw new TimeoutException($"进程 {pid} 在 {timeoutMs}ms 内未退出");
        if (!GetExitCodeProcess(handle, out var code))
            throw new InvalidOperationException($"获取进程 {pid} 退出码失败");
        return code;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);
}
