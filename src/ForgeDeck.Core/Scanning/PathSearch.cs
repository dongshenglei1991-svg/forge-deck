namespace ForgeDeck.Core.Scanning;

public static class PathSearch
{
    public static readonly string[] CliExtensions = { ".exe", ".cmd", ".bat", ".ps1" };

    public static IEnumerable<string> Probe(string dir, string name, string[]? extensions = null)
    {
        extensions ??= CliExtensions;
        // 先按扩展名探测（.exe/.cmd/.bat/.ps1），最后才尝试无扩展名直命中——
        // 避免 npm 全局目录的 sh shim（无扩展名）抢先命中导致启动失败。
        foreach (var ext in extensions)
        {
            var withExt = Path.Combine(dir, name + ext);
            if (File.Exists(withExt)) yield return withExt;
        }
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) yield return direct;
    }

    public static IEnumerable<string> Probe(string dir, IEnumerable<string> names, string[]? extensions = null)
    {
        foreach (var name in names)
            foreach (var hit in Probe(dir, name, extensions))
                yield return hit;
    }

    public static IEnumerable<string> PathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? FindOnPath(string name)
    {
        foreach (var dir in PathDirectories())
            foreach (var hit in Probe(dir, name))
                return hit;
        return null;
    }
}
