using ForgeDeck.Core.Config;

namespace ForgeDeck.Core.Tests;

public class WorkdirHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "forgedeck-tests", $"{Guid.NewGuid():N}.json");
    private readonly ConfigStore _store;

    public WorkdirHistoryTests() { _store = new ConfigStore(_path); _store.Load(); }
    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public void Add_NewPath_Prepends()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a");
        service.Add(@"D:\b");
        Assert.Equal(new[] { @"D:\b", @"D:\a" }, service.List());
    }

    [Fact]
    public void Add_ExistingPath_MovesToFront_NoDuplicate()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a"); service.Add(@"D:\b"); service.Add(@"D:\a");
        Assert.Equal(new[] { @"D:\a", @"D:\b" }, service.List());
    }

    [Fact]
    public void Add_RespectsMaxHistory()
    {
        _store.Config.Settings.MaxWorkdirHistory = 3;
        var service = new WorkdirHistoryService(_store);
        for (var i = 1; i <= 5; i++) service.Add($@"D:\d{i}");
        Assert.Equal(new[] { @"D:\d5", @"D:\d4", @"D:\d3" }, service.List());
    }

    [Fact]
    public void Add_EmptyPath_Ignored()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add("   ");
        Assert.Empty(service.List());
    }

    [Fact]
    public void Add_PersistsToDisk()
    {
        new WorkdirHistoryService(_store).Add(@"D:\keep");
        var reloaded = new ConfigStore(_path);
        reloaded.Load();
        Assert.Contains(@"D:\keep", new WorkdirHistoryService(reloaded).List());
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        var service = new WorkdirHistoryService(_store);
        service.Add(@"D:\a"); service.Add(@"D:\b");
        service.Remove(@"D:\a");
        Assert.Equal(new[] { @"D:\b" }, service.List());
    }
}
