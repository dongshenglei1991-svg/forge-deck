using System.Diagnostics;
using System.Text;
using ForgeDeck.Core.Scanning;

namespace ForgeDeck.Core.Launching;

public sealed record LaunchCommand(string App, IReadOnlyList<string> Args);

public sealed class LaunchService
{
    /// <summary>引号感知的参数分词（支持 "..." 与 '...'，引号内保留空白）。</summary>
    public static IReadOnlyList<string> SplitArgs(string args)
    {
        var result = new List<string>();
        var i = 0;
        while (i < args.Length)
        {
            while (i < args.Length && char.IsWhiteSpace(args[i])) i++;
            if (i >= args.Length) break;
            var sb = new StringBuilder();
            while (i < args.Length && !char.IsWhiteSpace(args[i]))
            {
                var c = args[i];
                if (c is '"' or '\'')
                {
                    var quote = c;
                    i++;
                    while (i < args.Length && args[i] != quote) sb.Append(args[i++]);
                    i++; // 跳过闭合引号
                }
                else sb.Append(args[i++]);
            }
            result.Add(sb.ToString());
        }
        return result;
    }

    /// <summary>PowerShell 宿主三级回退：PATH 上的 pwsh → PATH 上的 powershell → System32 全路径。</summary>
    private static string ResolvePowerShellHost() =>
        PathSearch.FindOnPath("pwsh")
        ?? PathSearch.FindOnPath("powershell")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powershell.exe");

    /// <summary>分词结果 + AutoRestore 追加的 ResumeArgs（已含则不重复）——内嵌/外部双轨共用。</summary>
    private static List<string> EffectiveArgs(ToolInfo tool, LaunchProfile profile)
    {
        var args = SplitArgs(profile.Args).ToList();
        var known = KnownTools.MatchByExeName(tool.ExePath);
        if (profile.AutoRestore && known?.ResumeArgs is { } resume && !args.Contains(resume))
            args.Add(resume);
        return args;
    }

    /// <summary>含空白的参数重新加引号（避免重组命令行时裂成多个参数）。</summary>
    private static string QuoteIfSpaced(string token) =>
        token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    public LaunchCommand BuildCommand(ToolInfo tool, LaunchProfile profile)
    {
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        var args = EffectiveArgs(tool, profile);
        return ext switch
        {
            ".exe" => new LaunchCommand(tool.ExePath, args),
            ".cmd" or ".bat" => new LaunchCommand(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                new[] { "/c", tool.ExePath }.Concat(args).ToList()),
            ".ps1" => new LaunchCommand(
                ResolvePowerShellHost(),
                new[] { "-File", tool.ExePath }.Concat(args).ToList()),
            _ => throw new NotSupportedException($"不支持的启动文件类型：{ext}"),
        };
    }

    public void Validate(ToolInfo tool, LaunchProfile profile)
    {
        if (!File.Exists(tool.ExePath))
            throw new InvalidOperationException($"可执行文件不存在：{tool.ExePath}");
        var workdir = ResolveWorkdir(profile);
        if (!Directory.Exists(workdir))
            throw new InvalidOperationException($"工作目录不存在：{workdir}");
    }

    public static string ResolveWorkdir(LaunchProfile profile) =>
        profile.Workdir.Trim().Length == 0
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.ExpandEnvironmentVariables(profile.Workdir.Trim());

    public IReadOnlyDictionary<string, string> ResolveEnv(LaunchProfile profile)
    {
        var env = new Dictionary<string, string>();
        foreach (var (key, value) in profile.Env)
        {
            if (key.Trim().Length == 0) continue;
            env[key.Trim()] = Environment.ExpandEnvironmentVariables(value);
        }
        return env;
    }

    public ProcessStartInfo BuildExternalStartInfo(ToolInfo tool, LaunchProfile profile)
    {
        Validate(tool, profile);
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        // 外部轨道：分词重组（AutoRestore 追加 ResumeArgs），含空白的参数重新引用；
        // .ps1 由 PowerShell 宿主包装执行（CreateProcess 无法直接执行脚本，与内嵌轨道语义一致）。
        var joined = string.Join(' ', EffectiveArgs(tool, profile).Select(QuoteIfSpaced));
        var psi = new ProcessStartInfo
        {
            FileName = ext == ".ps1" ? ResolvePowerShellHost() : tool.ExePath,
            Arguments = ext == ".ps1" ? $"-File \"{tool.ExePath}\"{(joined.Length > 0 ? " " + joined : "")}" : joined,
            WorkingDirectory = ResolveWorkdir(profile),
            UseShellExecute = false,
        };
        foreach (var (key, value) in ResolveEnv(profile))
            psi.EnvironmentVariables[key] = value;
        return psi;
    }

    public int LaunchExternal(ToolInfo tool, LaunchProfile profile)
    {
        using var process = Process.Start(BuildExternalStartInfo(tool, profile))
            ?? throw new InvalidOperationException("进程启动失败");
        return process.Id;
    }
}
