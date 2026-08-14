using System.Diagnostics;
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
        Assert.Equal("--model \"sonnet 4\"", psi.Arguments);
        Assert.Equal(_dir, psi.WorkingDirectory);
        Assert.Equal("V", psi.EnvironmentVariables["K"]);
        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void LaunchExternal_CmdExitsWithCode()
    {
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var tool = Tool(cmdPath);
        var profile = Profile("/c exit 3", _dir);
        using var process = Process.Start(_service.BuildExternalStartInfo(tool, profile))!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(3, process.ExitCode);
    }
}
