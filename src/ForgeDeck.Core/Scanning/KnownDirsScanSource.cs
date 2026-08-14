namespace ForgeDeck.Core.Scanning;

public class KnownDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInHints(tool) ?? FindInExtraDirs(tool, context.ExtraDirs);
            if (hit != null) yield return hit;
        }
    }

    private static ScanHit? FindInHints(KnownTool tool)
    {
        foreach (var hint in tool.Hints)
        {
            var dir = Environment.ExpandEnvironmentVariables(hint.Pattern);
            if (!Directory.Exists(dir)) continue;
            var path = PathSearch.Probe(dir, tool.ExeNames).FirstOrDefault();
            if (path != null) return new ScanHit(Path.GetFullPath(path), tool, hint.Label);
        }
        return null;
    }

    private static ScanHit? FindInExtraDirs(KnownTool tool, IReadOnlyList<string> extraDirs)
    {
        foreach (var extra in extraDirs)
        {
            if (string.IsNullOrWhiteSpace(extra) || !Directory.Exists(extra)) continue;
            var path = PathSearch.Probe(extra, tool.ExeNames).FirstOrDefault();
            if (path != null) return new ScanHit(Path.GetFullPath(path), tool, "附加目录");
        }
        return null;
    }
}
