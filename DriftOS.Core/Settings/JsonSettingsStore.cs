using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriftOS.Core.Settings
{
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private static string AppDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriftOS");

        public static string ConfigPath => Path.Combine(AppDir, "config.json");
        private static string LegacyPath => Path.Combine(AppDir, "settings.json"); // read-only migration

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public SettingsModel Load()
        {
            try
            {
                Directory.CreateDirectory(AppDir);
                var path = File.Exists(ConfigPath) ? ConfigPath :
                           File.Exists(LegacyPath) ? LegacyPath :
                           ConfigPath;

                if (File.Exists(path))
                {
                    var txt = File.ReadAllText(path);
                    var model = JsonSerializer.Deserialize<SettingsModel>(txt, JsonOpts) ?? new SettingsModel();
                    Migrate(model);
                    return model;
                }
            }
            catch { /* fall through to defaults */ }

            var fresh = new SettingsModel();
            Migrate(fresh);
            return fresh;
        }

        public void Save(SettingsModel settings)
        {
            Directory.CreateDirectory(AppDir);
            Migrate(settings);
            var json = JsonSerializer.Serialize(settings, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }

        private static void Migrate(SettingsModel s)
        {
            if (s.PointerSpeed <= 0) s.PointerSpeed = s.Sensitivity > 0 ? s.Sensitivity : 1.0;
            if (s.ScrollSpeedV <= 0) s.ScrollSpeedV = s.PointerSpeed;
            if (s.ScrollSpeedH <= 0) s.ScrollSpeedH = s.PointerSpeed;
            if (s.PointerAlpha <= 0) s.PointerAlpha = 0.35;
            if (s.ScrollAlpha <= 0) s.ScrollAlpha = 0.50;
            if (s.ScrollGamma <= 0) s.ScrollGamma = 1.60;
            s.Deadzone = Math.Clamp(s.Deadzone, 0.0, 0.30);
        }
    }
}
