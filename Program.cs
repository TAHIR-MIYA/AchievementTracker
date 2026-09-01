using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    class Program
    {
        static string dictionaryPath = "achievement_dictionary.json";
        static string iconsPath = "achievement_icons.json";
        
        static Dictionary<string, string> achievementNames = new Dictionary<string, string>();
        static Dictionary<string, string> achievementIcons = new Dictionary<string, string>();
        static Dictionary<string, bool> previousState = new Dictionary<string, bool>();

        static NotifyIcon? trayIcon;
        static MainWindow? dashboard;
        static List<FileSystemWatcher> activeWatchers = new List<FileSystemWatcher>();
        static readonly HttpClient httpClient = new HttpClient();

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SetupSystemTray();
            LoadTranslator();

            dashboard = new MainWindow();
            UpdateWatchers(dashboard.GetTrackedGames());
            
            TriggerDataDownload(dashboard.GetTrackedGames(), dashboard.SavedApiKey);

            // Trigger an automatic cloud backup on startup if path is configured
            if (!string.IsNullOrWhiteSpace(dashboard.SavedCloudPath))
            {
                Task.Run(() => PerformCloudBackups(dashboard.GetTrackedGames(), dashboard.SavedCloudPath));
            }

            Application.Run(dashboard); 
        }

        static void SetupSystemTray()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = File.Exists("app_icon.ico") ? new Icon("app_icon.ico") : SystemIcons.Application,
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "Universal Achievement Tracker"
            };

            trayIcon.DoubleClick += (s, e) => { if (dashboard != null) { dashboard.Show(); dashboard.WindowState = FormWindowState.Normal; } };

            trayIcon.ContextMenuStrip.Items.Add("Open Dashboard", null, (s, e) => { if (dashboard != null) { dashboard.Show(); dashboard.WindowState = FormWindowState.Normal; } });
            trayIcon.ContextMenuStrip.Items.Add("-"); 
            trayIcon.ContextMenuStrip.Items.Add("Exit Tracker", null, (s, e) =>
            {
                if (trayIcon != null) trayIcon.Visible = false;
                Environment.Exit(0); 
            });
        }

        public static void UpdateWatchers(List<TrackedGame> games)
        {
            foreach (var watcher in activeWatchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            activeWatchers.Clear();

            string publicDocs = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

            foreach (var game in games)
            {
                // 1. WATCH GOLDBERG
                string goldbergDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", game.AppId);
                Directory.CreateDirectory(goldbergDir); 
                LoadBaselineState(game.AppId, Path.Combine(goldbergDir, "achievements.json"));
                AttachWatcher(goldbergDir, "*.json", (s, e) => OnAchievementFileChanged(s, e, game.AppId));

                // 2. WATCH ALL INI EMULATORS (CODEX, RUNE, FLT, TENOKE, OnlineFix)
                string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"Steam\TENOKE", @"OnlineFix" };
                
                foreach (string emu in emuFolders)
                {
                    string emuDir = Path.Combine(publicDocs, "Documents", emu, game.AppId);
                    Directory.CreateDirectory(emuDir);

                    string iniPath = Path.Combine(emuDir, "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "remote", "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "achievements"); 
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "Stats", "Achievements"); 
                    
                    ParseIniForBaseline(game.AppId, iniPath);

                    AttachWatcher(emuDir, "*.*", (s, e) => {
                        string lowerName = e.Name?.ToLower() ?? "";
                        if (lowerName.Contains("achievement") || lowerName.Contains("stats")) 
                            ParseIniForChanges(game.AppId, e.FullPath);
                    });
                }
            }
        }

        public static int PerformCloudBackups(List<TrackedGame> games, string cloudDestinationPath)
        {
            if (string.IsNullOrWhiteSpace(cloudDestinationPath)) return 0;
            int successCount = 0;
            string publicDocs = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            try
            {
                Directory.CreateDirectory(cloudDestinationPath);
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

                foreach (var game in games)
                {
                    string tempStage = Path.Combine(Path.GetTempPath(), "TrackerBackupStage_" + Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempStage);

                    bool gameHasFiles = false;
                    string gameStageDir = Path.Combine(tempStage, $"Backup_{game.Name}_{game.AppId}");
                    Directory.CreateDirectory(gameStageDir);

                    // 1. Check Goldberg save folder
                    string goldbergDir = Path.Combine(appData, "Goldberg SteamEmu Saves", game.AppId);
                    if (Directory.Exists(goldbergDir))
                    {
                        CopyDirectory(goldbergDir, Path.Combine(gameStageDir, "Goldberg"));
                        gameHasFiles = true;
                    }

                    // 2. Check INI Emu save folders
                    string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"Steam\TENOKE", @"OnlineFix" };
                    foreach (string emu in emuFolders)
                    {
                        string emuDir = Path.Combine(publicDocs, "Documents", emu, game.AppId);
                        if (Directory.Exists(emuDir))
                        {
                            CopyDirectory(emuDir, Path.Combine(gameStageDir, emu.Replace(@"\", "_")));
                            gameHasFiles = true;
                        }
                    }

                    // 3. Special Case: Elden Ring (.sl2 saves in AppData\Roaming\EldenRing)
                    if (game.AppId == "1245620" || game.Name.Contains("Elden Ring", StringComparison.OrdinalIgnoreCase))
                    {
                        string eldenRingRoaming = Path.Combine(appData, "EldenRing");
                        if (Directory.Exists(eldenRingRoaming))
                        {
                            CopyDirectory(eldenRingRoaming, Path.Combine(gameStageDir, "EldenRing_Saves"));
                            gameHasFiles = true;
                        }
                    }

                    if (gameHasFiles)
                    {
                        // Clean filename safe characters
                        string safeGameName = string.Concat(game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
                        string zipFilePath = Path.Combine(cloudDestinationPath, $"Save_{safeGameName}_{timestamp}.zip");

                        if (File.Exists(zipFilePath)) File.Delete(zipFilePath);
                        ZipFile.CreateFromDirectory(gameStageDir, zipFilePath, CompressionLevel.Optimal, false);

                        // ROTATION CLEANUP: Keep only the 5 most recent backups for THIS game in the cloud folder
                        var backupFiles = new DirectoryInfo(cloudDestinationPath)
                            .GetFiles($"Save_{safeGameName}_*.zip")
                            .OrderByDescending(f => f.CreationTime)
                            .ToList();

                        if (backupFiles.Count > 5)
                        {
                            for (int i = 5; i < backupFiles.Count; i++)
                            {
                                try { backupFiles[i].Delete(); } catch { }
                            }
                        }

                        successCount++;
                    }

                    try { Directory.Delete(tempStage, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cloud Backup Error] {ex.Message}");
            }

            return successCount;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, true);
            }
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, targetSubDir);
            }
        }

        static void AttachWatcher(string directory, string filter, FileSystemEventHandler onChanged)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(directory, filter)
            {
                NotifyFilter = NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };
            watcher.Changed += onChanged;
            activeWatchers.Add(watcher);
        }

        static void LoadBaselineState(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var sr = new StreamReader(fs);
                        using JsonDocument doc = JsonDocument.Parse(sr.ReadToEnd());
                        foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                        {
                            if (ExtractAchievementStatus(prop.Value)) previousState[appId + "_" + prop.Name] = true;
                        }
                        break;
                    }
                    catch { Thread.Sleep(50); }
                }
            } catch { }
        }

        static void OnAchievementFileChanged(object sender, FileSystemEventArgs e, string appId)
        {
            Thread.Sleep(50); 
            try
            {
                using var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                using JsonDocument doc = JsonDocument.Parse(sr.ReadToEnd());
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    string achKey = prop.Name;
                    bool isEarned = ExtractAchievementStatus(prop.Value);
                    string stateKey = appId + "_" + achKey; 

                    if (isEarned && (!previousState.ContainsKey(stateKey) || !previousState[stateKey]))
                    {
                        previousState[stateKey] = true; 
                        TriggerUI(appId, achKey);

                        if (dashboard != null && !string.IsNullOrWhiteSpace(dashboard.SavedCloudPath))
                        {
                            Task.Run(() => PerformCloudBackups(dashboard.GetTrackedGames(), dashboard.SavedCloudPath));
                        }
                    }
                    else if (!isEarned && previousState.ContainsKey(stateKey))
                    {
                        previousState[stateKey] = false; 
                    }
                }
            } catch { }
        }

        static bool ExtractAchievementStatus(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("earned", out JsonElement earned)) return earned.GetBoolean(); 
            else if (element.ValueKind == JsonValueKind.True) return true; 
            return false;
        }

        static void ParseIniForBaseline(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string? line;
                string currentSection = "";

                while ((line = sr.ReadLine()) != null)
                {
                    string originalLine = line.Trim();
                    string tLine = originalLine.ToLower();

                    if (tLine.StartsWith("[") && tLine.EndsWith("]")) 
                        currentSection = originalLine.Substring(1, originalLine.Length - 2);
                    else if ((tLine == "achieved=1" || tLine.EndsWith("=1") || tLine == "achieved=true" || tLine.EndsWith("=true")) && !string.IsNullOrEmpty(currentSection) && tLine != "steamachievements")
                    {
                        string achKey = originalLine.Contains("=") && tLine.Split('=')[0] != "achieved" ? originalLine.Split('=')[0] : currentSection;
                        previousState[appId + "_" + achKey] = true; 
                    }
                }
            } catch { }
        }

        static void ParseIniForChanges(string appId, string filePath)
        {
            Thread.Sleep(50);
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string? line;
                string currentSection = "";

                while ((line = sr.ReadLine()) != null)
                {
                    string originalLine = line.Trim();
                    string tLine = originalLine.ToLower();

                    if (tLine.StartsWith("[") && tLine.EndsWith("]")) 
                        currentSection = originalLine.Substring(1, originalLine.Length - 2);
                    else if ((tLine == "achieved=1" || tLine.EndsWith("=1") || tLine == "achieved=true" || tLine.EndsWith("=true")) && !string.IsNullOrEmpty(currentSection) && tLine != "steamachievements")
                    {
                        string achKey = originalLine.Contains("=") && tLine.Split('=')[0] != "achieved" ? originalLine.Split('=')[0] : currentSection;
                        string stateKey = appId + "_" + achKey; 

                        if (!previousState.ContainsKey(stateKey) || !previousState[stateKey])
                        {
                            previousState[stateKey] = true; 
                            TriggerUI(appId, achKey);

                            if (dashboard != null && !string.IsNullOrWhiteSpace(dashboard.SavedCloudPath))
                            {
                                Task.Run(() => PerformCloudBackups(dashboard.GetTrackedGames(), dashboard.SavedCloudPath));
                            }
                        }
                    }
                    else if ((tLine == "achieved=0" || tLine.EndsWith("=0") || tLine == "achieved=false" || tLine.EndsWith("=false")) && !string.IsNullOrEmpty(currentSection))
                    {
                        string achKey = originalLine.Contains("=") && tLine.Split('=')[0] != "achieved" ? originalLine.Split('=')[0] : currentSection;
                        string stateKey = appId + "_" + achKey; 
                        previousState[stateKey] = false; 
                    }
                }
            } catch { }
        }

        static void TriggerUI(string appId, string achKey)
        {
            string lookupKey = appId + "_" + achKey;
            string displayName = achievementNames.ContainsKey(lookupKey) ? achievementNames[lookupKey] : achKey;
            string iconUrl = achievementIcons.ContainsKey(lookupKey) ? achievementIcons[lookupKey] : "";
            
            ShowPopup(displayName, iconUrl);
        }

        static void LoadTranslator()
        {
            if (File.Exists(dictionaryPath)) {
                try {
                    string json = File.ReadAllText(dictionaryPath);
                    if (!string.IsNullOrWhiteSpace(json)) achievementNames = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                } catch { achievementNames = new Dictionary<string, string>(); }
            }
            
            if (File.Exists(iconsPath)) {
                try {
                    string json = File.ReadAllText(iconsPath);
                    if (!string.IsNullOrWhiteSpace(json)) achievementIcons = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                } catch { achievementIcons = new Dictionary<string, string>(); }
            }
        }

        public static void TriggerDataDownload(List<TrackedGame> games, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            Task.Run(() => DownloadAllGameData(games, apiKey));
        }

        static async Task DownloadAllGameData(List<TrackedGame> games, string apiKey)
        {
            foreach (var game in games) await FetchSteamData(game.AppId, apiKey);
            
            File.WriteAllText(dictionaryPath, JsonSerializer.Serialize(achievementNames, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(iconsPath, JsonSerializer.Serialize(achievementIcons, new JsonSerializerOptions { WriteIndented = true }));
        }

        static async Task FetchSteamData(string appId, string apiKey)
        {
            try
            {
                string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("game", out JsonElement gameElement) && gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) && statsElement.TryGetProperty("achievements", out JsonElement achievementsElement))
                    {
                        foreach (JsonElement ach in achievementsElement.EnumerateArray())
                        {
                            string internalName = ach.GetProperty("name").GetString() ?? "";
                            string displayName = ach.GetProperty("displayName").GetString() ?? internalName;
                            string iconUrl = ach.TryGetProperty("icon", out JsonElement iconEl) ? iconEl.GetString() ?? "" : "";
                            
                            achievementNames[$"{appId}_{internalName}"] = displayName;
                            achievementIcons[$"{appId}_{internalName}"] = iconUrl;
                        }
                    }
                }
            } catch { }
        }

        static void ShowPopup(string achievementName, string iconUrl)
        {
            Thread uiThread = new Thread(() =>
            {
                OverlayWindow window = new OverlayWindow(achievementName, iconUrl);
                Application.Run(window);
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
        }
    }
}