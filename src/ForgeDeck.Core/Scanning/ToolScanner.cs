namespace ForgeDeck.Core.Scanning;

public sealed class ToolScanner
{
    private readonly IEnumerable<IScanSource> _sources;

    /// <summary>sources 枚举顺序即优先级，先命中者胜（组合根注入顺序：KnownDirs→Path→Registry→StartMenu→ExtraDirs）。</summary>
    public ToolScanner(IEnumerable<IScanSource> sources) => _sources = sources;

    public List<ToolInfo> Scan(ScanContext context)
    {
        var byPath = new Dictionary<string, ToolInfo>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in _sources)
        {
            List<ScanHit> hits;
            try
            {
                // 立即枚举，使源在枚举期间抛出的异常也纳入隔离范围（规格 §7）
                hits = source.Scan(context).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ForgeDeck] 扫描源 {source.GetType().Name} 失败，已跳过：{ex.Message}");
                continue;
            }
            foreach (var hit in hits)
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
        }
        return byPath.Values
            .OrderByDescending(t => t.Builtin)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
