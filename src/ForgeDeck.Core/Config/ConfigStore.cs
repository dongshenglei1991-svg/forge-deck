using System.Text.Json;
using ForgeDeck.Core;

namespace ForgeDeck.Core.Config;

public sealed class ConfigStore
{
    private readonly string _path;
    public AppConfig Config { get; private set; } = new();

    public ConfigStore(string? path = null)
    {
        _path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ForgeDeck", "config.json");
    }

    public void Load()
    {
        if (!File.Exists(_path)) { Config = new AppConfig(); return; }
        try
        {
            Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions.Default)
                     ?? new AppConfig();
        }
        catch (JsonException)
        {
            File.Move(_path, _path + ".bak", overwrite: true);
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Config, JsonOptions.Default));
        File.Move(tmp, _path, overwrite: true);
    }
}
