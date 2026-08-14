namespace ForgeDeck.Core.Scanning;

public static class PathSearch
{
    public static readonly string[] CliExtensions = { ".exe", ".cmd", ".bat", ".ps1" };

    public static IEnumerable<string> Probe(string dir, string name, string[]? extensions = null)
    {
        extensions ??= CliExtensions;
        var direct = Path.Combine(dir, name);
        if (File.Exists(direct)) yield return direct;
        foreach (var ext in extensions)
        {
            var withExt = Path.Combine(dir, name + ext);
            if (File.Exists(withExt)) yield return withExt;
        }
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
