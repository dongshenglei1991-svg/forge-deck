namespace ForgeDeck.Core.Scanning;

public sealed record ScanContext(IReadOnlyList<string> ExtraDirs);

public sealed record ScanHit(string ExePath, KnownTool? Known, string SourceLabel);

public interface IScanSource
{
    IEnumerable<ScanHit> Scan(ScanContext context);
}
