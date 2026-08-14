namespace ForgeDeck.Core.Config;

public sealed class WorkdirHistoryService(ConfigStore store)
{
    public const string GlobalKey = "__global__";

    public IReadOnlyList<string> List() =>
        store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list)
            ? list
            : Array.Empty<string>();

    public void Add(string path)
    {
        path = path.Trim();
        if (path.Length == 0) return;
        var list = Ensure();
        list.Remove(path);
        list.Insert(0, path);
        var max = Math.Max(1, store.Config.Settings.MaxWorkdirHistory);
        if (list.Count > max) list.RemoveRange(max, list.Count - max);
        store.Save();
    }

    public void Remove(string path)
    {
        if (store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list) && list.Remove(path.Trim()))
            store.Save();
    }

    private List<string> Ensure()
    {
        if (!store.Config.WorkdirHistory.TryGetValue(GlobalKey, out var list))
        {
            list = new List<string>();
            store.Config.WorkdirHistory[GlobalKey] = list;
        }
        return list;
    }
}
