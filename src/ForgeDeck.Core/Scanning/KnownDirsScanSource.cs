namespace ForgeDeck.Core.Scanning;

public class KnownDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInHints(tool);
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
}
