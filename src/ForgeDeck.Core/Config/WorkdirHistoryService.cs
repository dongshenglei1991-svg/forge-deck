namespace ForgeDeck.Core.Config;

public sealed class WorkdirHistoryService(ConfigStore store)
{
    public const string GlobalKey = "__global__";

    public IReadOnlyList<string> List() =>
        store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) && list is not null
            ? list.ToList()
            : Array.Empty<string>();

    public void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        path = path.Trim();
        var list = Ensure();
        list.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        var max = Math.Max(1, store.Config.Settings.MaxWorkdirHistory);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        store.Save();
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) && list is not null
            && list.RemoveAll(x => string.Equals(x, path.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
            store.Save();
    }

    private List<string> Ensure()
    {
        if (!store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) || list is null)
        {
            list = new List<string>();
            store.Config.WorkdirHistory[GlobalKey] = list;
        }
        return list;
    }
}
