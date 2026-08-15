namespace ForgeDeck.Core.Scanning;

public sealed class PathScanSource : IScanSource
{
    public IEnumerable<ScanHit> Scan(ScanContext context)
    {
        foreach (var tool in KnownTools.All)
        {
            var hit = FindTool(tool);
            if (hit != null) yield return hit;
        }
    }

    internal static ScanHit? FindTool(KnownTool tool)
    {
        foreach (var exe in tool.ExeNames)
            foreach (var dir in PathSearch.PathDirectories())
                foreach (var path in PathSearch.Probe(dir, exe))
                    return new ScanHit(Path.GetFullPath(path), tool, "PATH");
        return null;
    }
}
