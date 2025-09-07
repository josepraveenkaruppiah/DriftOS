using System.Text.Json;

namespace DriftOS.Core.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;

    public JsonSettingsStore(string? path = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _path = path ?? Path.Combine(appData, "DriftOS", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
    }

    public SettingsModel Load()
    {
        if (!File.Exists(_path)) return new SettingsModel();
        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
    }

    public void Save(SettingsModel model)
    {
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }
}
