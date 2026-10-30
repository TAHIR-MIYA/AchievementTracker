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
        public string PopupPosition { get; set; } = "top-right";   // top-left, top-right, bottom-left, bottom-right
        public int PopupSeconds { get; set; } = 5;       // how long the popup stays fully visible
        public bool ScreenshotOnUnlock { get; set; } = false;   // save a screenshot of the game when something unlocks
        public string BackupFolder { get; set; } = "";           // where backup zips go (can be inside Google Drive, OneDrive...)
        public bool AutoBackup { get; set; } = false;
        public DateTime LastBackupUtc { get; set; } = DateTime.MinValue;

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
            settings.PopupSeconds = Math.Max(2, Math.Min(20, settings.PopupSeconds));
            string[] validPositions = { "top-left", "top-right", "bottom-left", "bottom-right" };
            if (Array.IndexOf(validPositions, settings.PopupPosition) < 0) settings.PopupPosition = "top-right";
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
