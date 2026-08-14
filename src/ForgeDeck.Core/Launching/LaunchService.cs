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

    public LaunchCommand BuildCommand(ToolInfo tool, LaunchProfile profile)
    {
        var ext = Path.GetExtension(tool.ExePath).ToLowerInvariant();
        var args = SplitArgs(profile.Args).ToList();
        var known = KnownTools.MatchByExeName(tool.ExePath);
        if (profile.AutoRestore && known?.ResumeArgs is { } resume && !args.Contains(resume))
            args.Add(resume);
        return ext switch
        {
            ".exe" => new LaunchCommand(tool.ExePath, args),
            ".cmd" or ".bat" => new LaunchCommand(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                new[] { "/c", tool.ExePath }.Concat(args).ToList()),
            ".ps1" => new LaunchCommand(
                PathSearch.FindOnPath("pwsh") ?? "powershell.exe",
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
        var psi = new ProcessStartInfo
        {
            FileName = tool.ExePath,
            Arguments = profile.Args,
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
