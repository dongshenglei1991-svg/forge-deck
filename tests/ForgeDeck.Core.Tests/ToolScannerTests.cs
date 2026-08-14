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

    private sealed class ThrowingSource : IScanSource
    {
        // 迭代器形式：先产出一条指向不存在路径的假 hit，再在枚举（MoveNext）期间抛异常，
        // 使 Scan_ContinuesWhenSourceThrows 覆盖 ToolScanner 立即枚举期间的异常隔离路径。
        public IEnumerable<ScanHit> Scan(ScanContext context)
        {
            yield return new ScanHit(Path.Combine(Path.GetTempPath(), "forgedeck-ghost.exe"), null, "爆炸源");
            throw new InvalidOperationException("源爆炸");
        }
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
    public void Scan_ContinuesWhenSourceThrows()
    {
        var claude = FakeExe("claude.cmd");
        var scanner = new ToolScanner(new IScanSource[]
        {
            new ThrowingSource(),
            new FakeSource(new ScanHit(claude, Claude, "PATH")),
        });
        var tools = scanner.Scan(new ScanContext(Array.Empty<string>()));
        var tool = Assert.Single(tools);
        Assert.Equal("Claude Code", tool.Name);
        Assert.Equal("PATH", tool.Source);
    }

    [Fact]
    public void Probe_PrefersExtensionOverBareName()
    {
        var dir = Path.Combine(_dir, "shim");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");      // npm sh shim（无扩展名）
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var first = PathSearch.Probe(dir, "claude").First();
        Assert.EndsWith("claude.cmd", first);
    }

    [Fact]
    public void FindOnPath_PrefersCmdOverBareShim()
    {
        var dir = Path.Combine(_dir, "onpath");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir);
        try
        {
            var found = PathSearch.FindOnPath("claude");
            Assert.NotNull(found);
            Assert.EndsWith("claude.cmd", found);
        }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }

    [Fact]
    public void PathScanSource_FindsKnownTool_PrefersCmdOverBareShim()
    {
        var dir = Path.Combine(_dir, "onpath2");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "claude"), "");
        File.WriteAllText(Path.Combine(dir, "claude.cmd"), "");
        var original = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", dir);
        try
        {
            var hit = Assert.Single(new PathScanSource().Scan(new ScanContext(Array.Empty<string>())));
            Assert.Equal("Claude Code", hit.Known!.Name);
            Assert.Equal("PATH", hit.SourceLabel);
            Assert.EndsWith("claude.cmd", hit.ExePath);
        }
        finally { Environment.SetEnvironmentVariable("PATH", original); }
    }

    [Fact]
    public void MatchByName_PrefersLongerName()
    {
        var tool = KnownTools.MatchByName("Cursor Agent");
        Assert.NotNull(tool);
        Assert.Equal("Cursor Agent", tool.Name);
    }

    [Fact]
    public void KnownDirs_FindsToolInHintDir()
    {
        var hintDir = Path.Combine(_dir, "npm");
        Directory.CreateDirectory(hintDir);
        File.WriteAllText(Path.Combine(hintDir, "claude.cmd"), "");

        Environment.SetEnvironmentVariable("FD_TEST_NPM", hintDir);
        try
        {
            var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
                new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM%", "npm 全局") });
            var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
                new[] { "codex" }, Array.Empty<InstallHint>());
            var source = new KnownDirsScanSourceForTest(new[] { testTool, codexTool });

            // Codex 无 hint 不命中；Claude 命中 hint 目录
            var claudeHit = Assert.Single(source.Scan(new ScanContext(Array.Empty<string>())));
            Assert.Equal("npm 全局", claudeHit.SourceLabel);
            Assert.EndsWith("claude.cmd", claudeHit.ExePath);
        }
        finally { Environment.SetEnvironmentVariable("FD_TEST_NPM", null); }
    }

    [Fact]
    public void ExtraDirs_FindsToolWithoutHint()
    {
        var extraDir = Path.Combine(_dir, "extra");
        Directory.CreateDirectory(extraDir);
        File.WriteAllText(Path.Combine(extraDir, "codex.exe"), "");

        var testTool = new KnownTool("Claude Code", ToolType.Cli, "C/", null,
            new[] { "claude" }, new[] { new InstallHint("%FD_TEST_NPM_MISSING%", "npm 全局") });
        var codexTool = new KnownTool("Codex CLI", ToolType.Cli, "CX", null,
            new[] { "codex" }, Array.Empty<InstallHint>());
        var source = new ExtraDirsScanSourceForTest(new[] { testTool, codexTool });

        // Claude 的 hint 目录不存在且附加目录里没有 claude；Codex 由附加目录兜底命中
        var hit = Assert.Single(source.Scan(new ScanContext(new[] { extraDir })));
        Assert.Equal("Codex CLI", hit.Known!.Name);
        Assert.Equal("附加目录", hit.SourceLabel);
        Assert.EndsWith("codex.exe", hit.ExePath);
    }
}

file sealed class KnownDirsScanSourceForTest(KnownTool[] tools) : KnownDirsScanSource
{
    protected override IEnumerable<KnownTool> Catalog => tools;
}

file sealed class ExtraDirsScanSourceForTest(KnownTool[] tools) : ExtraDirsScanSource
{
    protected override IEnumerable<KnownTool> Catalog => tools;
}
