namespace ForgeDeck.Core.Scanning;

public sealed record InstallHint(string Pattern, string Label);

public sealed record KnownTool(
    string Name, ToolType Type, string Logo, string? ResumeArgs,
    string[] ExeNames, InstallHint[] Hints);

public static class KnownTools
{
    public static readonly IReadOnlyList<KnownTool> All = new KnownTool[]
    {
        new("Claude Code", ToolType.Cli, "C/", "--continue",
            new[] { "claude" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.claude\local", "用户目录") }),
        new("Codex CLI", ToolType.Cli, "CX", null,
            new[] { "codex" },
            new[] { new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录"),
                    new InstallHint(@"%APPDATA%\npm", "npm 全局") }),
        new("Gemini CLI", ToolType.Cli, "G", "--resume",
            new[] { "gemini" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局") }),
        new("Grok Build", ToolType.Cli, "GB", "--continue",
            new[] { "grok" },
            new[] { new InstallHint(@"%USERPROFILE%\.grok\bin", "用户目录") }),
        new("OpenCode", ToolType.Cli, "OC", "--continue",
            new[] { "opencode" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.opencode\bin", "用户目录"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("GitHub Copilot CLI", ToolType.Cli, "GH", "--continue",
            new[] { "copilot" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%LOCALAPPDATA%\GitHubCopilotCLI", "用户目录") }),
        new("Qwen Code", ToolType.Cli, "Qw", "--continue",
            new[] { "qwen" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.qwen\bin", "用户目录"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("Goose", ToolType.Cli, "Go", null,
            new[] { "goose" },
            new[] { new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("Amp", ToolType.Cli, "Am", null,
            new[] { "amp" },
            new[] { new InstallHint(@"%USERPROFILE%\.amp\bin", "用户目录"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("Crush", ToolType.Cli, "Cr", null,
            new[] { "crush" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("Continue CLI", ToolType.Cli, "Cn", "--resume",
            new[] { "cn" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("Kiro CLI", ToolType.Cli, "Ki", null,
            new[] { "kiro-cli" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Kiro CLI", "用户目录"),
                    new InstallHint(@"%USERPROFILE%\.local\bin", "用户目录") }),
        new("iFlow CLI", ToolType.Cli, "iF", null,
            new[] { "iflow" },
            new[] { new InstallHint(@"%APPDATA%\npm", "npm 全局") }),
        new("Aider", ToolType.Cli, "Ai", null,
            new[] { "aider" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Python\Scripts", "Python Scripts"),
                    new InstallHint(@"%APPDATA%\Python\Scripts", "Python Scripts") }),
        new("Cursor", ToolType.Gui, "Cu", null,
            new[] { "Cursor" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Cursor", "用户目录") }),
        new("Cursor Agent", ToolType.Cli, "Cu", null,
            new[] { "cursor-agent" },
            new[] { new InstallHint(@"%PROGRAMFILES%\Cursor\resources\app\bin", "开始菜单") }),
        new("Windsurf", ToolType.Gui, "W", null,
            new[] { "Windsurf" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Windsurf", "用户目录") }),
        new("Trae", ToolType.Gui, "T", null,
            new[] { "Trae" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Trae", "用户目录") }),
        new("Zed", ToolType.Gui, "Z", null,
            new[] { "zed", "Zed" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Zed", "用户目录") }),
        new("VS Code", ToolType.Gui, "VS", null,
            new[] { "Code" },
            new[] { new InstallHint(@"%LOCALAPPDATA%\Programs\Microsoft VS Code", "用户目录"),
                    new InstallHint(@"%PROGRAMFILES%\Microsoft VS Code", "用户目录") }),
    };

    public static KnownTool? MatchByExeName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return All.FirstOrDefault(t =>
            t.ExeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
    }

    public static KnownTool? MatchByName(string displayName) =>
        All.Where(t => displayName.Contains(t.Name, StringComparison.OrdinalIgnoreCase))
          .OrderByDescending(t => t.Name.Length)
          .FirstOrDefault();
}
