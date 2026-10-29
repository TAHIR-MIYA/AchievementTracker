using System;
using System.IO;
using System.Text.Json;

namespace AchievementTracker
{
    /// <summary>
    /// User settings. Stored in %APPDATA%\AchievementTracker so the Steam API key
    /// never sits next to the source code (and never gets committed to Git).
    /// </summary>
    public class AppSettings
    {
        public string SteamApiKey { get; set; } = "";
        public bool DesktopPopup { get; set; } = true;   // popup window drawn on your screen
        public bool ObsOverlay { get; set; } = true;     // local web page for an OBS Browser Source
        public int ObsPort { get; set; } = 5757;
        public int SoundVolume { get; set; } = 50;       // 0-100, unlock sound of the desktop popup

        public static AppSettings Current { get; private set; } = Load();

        private const string LegacyPath = "app_settings.json";

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AchievementTracker", "app_settings.json");

        private static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                }
                else if (File.Exists(LegacyPath))
                {
                    // One-time migration of the old settings file that lived next to the .exe
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(LegacyPath));
                    if (doc.RootElement.TryGetProperty("SteamApiKey", out JsonElement key))
                        settings.SteamApiKey = key.GetString() ?? "";
                    Write(settings);
                }
            }
            catch { }

            if (settings.ObsPort < 1024 || settings.ObsPort > 65535) settings.ObsPort = 5757;
            settings.SoundVolume = Math.Max(0, Math.Min(100, settings.SoundVolume));
            return settings;
        }

        public static void Save() => Write(Current);

        private static void Write(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
