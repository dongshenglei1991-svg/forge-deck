namespace ForgeDeck.Core;

public enum ToolType { Cli, Gui }
public enum OpenMode { Embedded, External }

public sealed class ToolInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public ToolType Type { get; set; } = ToolType.Cli;
    public string ExePath { get; set; } = "";
    public string Source { get; set; } = "";
    public bool Builtin { get; set; }
    public bool Manual { get; set; }
    public bool PathPinned { get; set; }
}

public sealed class LaunchProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ToolId { get; set; } = "";
    public string Name { get; set; } = "默认";
    public string Args { get; set; } = "";
    public Dictionary<string, string> Env { get; set; } = new();
    public string Workdir { get; set; } = "";
    public OpenMode OpenMode { get; set; } = OpenMode.Embedded;
    public bool AutoRestore { get; set; }
}

public sealed class AppSettings
{
    public string DefaultShell { get; set; } = "pwsh";
    public bool AutoScanOnStartup { get; set; } = true;
    public List<string> ExtraScanDirs { get; set; } = new();
    public bool SkipExitConfirm { get; set; }
    public bool PreferEmbedded { get; set; } = true;
    public int MaxWorkdirHistory { get; set; } = 20;
}

public sealed class LastUsedInfo
{
    public string ToolId { get; set; } = "";
    public string Workdir { get; set; } = "";
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public List<ToolInfo> Tools { get; set; } = new();
    public List<LaunchProfile> Profiles { get; set; } = new();
    public Dictionary<string, List<string>> WorkdirHistory { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public DateTime? LastScanAt { get; set; }
    public LastUsedInfo? LastUsed { get; set; }
    public List<string> HiddenExePaths { get; set; } = new();
    public Dictionary<string, string> LastProfileByTool { get; set; } = new();
}
