using ForgeDeck.Core;
using ForgeDeck.Core.Scanning;

namespace ForgeDeck.Core.Tests;

public class ToolScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "forgedeck-tests", Guid.NewGuid().ToString("N"));
    public ToolScannerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string FakeExe(string name, string? sub = null)
    {
        var dir = sub == null ? _dir : Path.Combine(_dir, sub);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    private sealed class FakeSource(params ScanHit[] hits) : IScanSource
    {
        public IEnumerable<ScanHit> Scan(ScanContext context) => hits;
    }

    private static readonly KnownTool Claude =
        new("Claude Code", ToolType.Cli, "C/", "--continue", new[] { "claude" }, Array.Empty<InstallHint>());

    [Fact]
    public void Scan_ReturnsHitTools_BuiltinFirst()
    {
        var claude = FakeExe("claude.cmd");
        var custom = FakeExe("mytool.exe");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(custom, null, "注册表")),
            new FakeSource(new ScanHit(claude, Claude, "npm 全局")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        Assert.Equal(2, tools.Count);
        Assert.Equal("Claude Code", tools[0].Name);       // builtin 排前
        Assert.True(tools[0].Builtin);
        Assert.Equal("mytool", tools[1].Name);
        Assert.False(tools[1].Builtin);
    }

    [Fact]
    public void Scan_SamePathFromTwoSources_KeepsFirst()
    {
        var claude = FakeExe("claude.cmd");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(claude, Claude, "npm 全局")),
            new FakeSource(new ScanHit(claude, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        var tool = Assert.Single(tools);
        Assert.Equal("npm 全局", tool.Source);
    }

    [Fact]
    public void Scan_SameKnownToolDifferentPaths_KeepsFirst()
    {
        var a = FakeExe("claude.cmd", "a");
        var b = FakeExe("claude.cmd", "b");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(a, Claude, "npm 全局"), new ScanHit(b, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        Assert.Single(tools);
        Assert.Equal(Path.GetFullPath(a), tools[0].ExePath);
    }

    [Fact]
    public void Scan_SkipsMissingFile()
    {
        var scanner = new ToolScanner(new IScanSource[]
        {
            new FakeSource(new ScanHit(Path.Combine(_dir, "ghost.exe"), Claude, "PATH")),
        });
        Assert.Empty(scanner.Scan(new ScanContext(Array.Empty<string>())));
    }

    [Fact]
    public void KnownDirs_FindsToolInHintDir_WithExtraDirsFallback()
    {
        var hintDir = Path.Combine(_dir, "npm");
        Directory.CreateDirectory(hintDir);
        File.WriteAllText(Path.Combine(hintDir, "claude.cmd"), "");
        var extraDir = Path.Combine(_dir, "extra");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "codex.exe"), "");

        Environment.SetEnvironmentVariable("FD_TEST_NPM", hintDir);
        try
        {
            var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
                new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM%", "npm 全局") });
            var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
                new[] { "codex" }, Array.Empty<InstallHint>());
            var source = new KnownDirsScanSourceForTest(new[] { testTool, codexTool });

            var hits = source.Scan(new ScanContext(new[] { extraDir })).ToList();
            var claudeHit = Assert.Single(hits, h => h.Known!.Name == "Claude Code");
            Assert.Equal("npm 全局", claudeHit.SourceLabel);
            Assert.EndsWith("claude.cmd", claudeHit.ExePath);
            var codexHit = Assert.Single(hits, h => h.Known!.Name == "Codex CLI");
            Assert.Equal("附加目录", codexHit.SourceLabel);
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_NPM", null); }
    }
}

file sealed class KnownDirsScanSourceForTest(KnownTool[] tools) : KnownDirsScanSource
{
    protected override IEnumerable<KnownTool> Catalog => tools;
}
