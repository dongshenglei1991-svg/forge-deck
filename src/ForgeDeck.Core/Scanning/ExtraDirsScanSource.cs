namespace ForgeDeck.Core.Scanning;

public class ExtraDirsScanSource : IScanSource
{
    protected virtual IEnumerable<KnownTool> Catalog => KnownTools.All;

    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in Catalog)
        {
            var hit = FindInExtraDirs(tool, context.ExtraDirs);
            if (hit != null) yield return hit;
        }
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
