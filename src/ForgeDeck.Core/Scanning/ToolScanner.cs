namespace ForgeDeck.Core.Scanning;

public sealed class ToolScanner
{
    private readonly IEnumerable<IScanSource> _sources;

    public ToolScanner(IEnumerable<IScanSource> sources) => _sources = sources;

    public List<ToolInfo> Scan(ScanContext context)
    {
        var byPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
            foreach (var hit in source.Scan(context))
            {
                if (!File.Exists(hit.ExePath)) continue;
                var path = Path.GetFullPath(hit.ExePath);
                if (byPath.ContainsKey(path)) continue;
                var name = hit.Known?.Name ?? Path.GetFileNameWithoutExtension(path);
                if (hit.Known != null && !seenNames.Add(name)) continue;
                byPath[path] = new ToolInfo
                {
                    Name = name,
                    Type = hit.Known?.Type ?? ToolType.Cli,
                    ExePath = path,
                    Source = hit.SourceLabel,
                    Builtin = hit.Known != null,
                };
            }
        return byPath.Values
            .OrderByDescending(t => t.Builtin)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
