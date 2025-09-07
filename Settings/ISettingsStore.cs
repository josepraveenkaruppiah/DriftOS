namespace DriftOS.Core.Settings;

public interface ISettingsStore
{
    SettingsModel Load();
    void Save(SettingsModel model);
}
