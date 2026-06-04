using System;
using System.Collections.Generic;
using System.IO;
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
        static Dictionary<string, string> achievementNames = new Dictionary<string, string>();
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

            Application.Run(dashboard); 
        }

        static void SetupSystemTray()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
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

            foreach (var game in games)
            {
                string goldbergDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", game.AppId);
                Directory.CreateDirectory(goldbergDir); 
                
                LoadBaselineState(game.AppId, Path.Combine(goldbergDir, "achievements.json"));

                FileSystemWatcher watcher = new FileSystemWatcher(goldbergDir, "achievements.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    EnableRaisingEvents = true
                };
                watcher.Changed += (sender, e) => OnAchievementFileChanged(sender, e, game.AppId);
                activeWatchers.Add(watcher);
            }
        }

        static void LoadBaselineState(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                using JsonDocument doc = JsonDocument.Parse(sr.ReadToEnd());
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (ExtractAchievementStatus(prop.Value)) 
                        previousState[appId + "_" + prop.Name] = true;
                }
            }
            catch { }
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
                        
                        string lookupKey = appId + "_" + achKey;
                        string displayName = achievementNames.ContainsKey(lookupKey) ? achievementNames[lookupKey] : achKey;
                        
                        ShowPopup(displayName);
                    }
                }
            }
            catch { }
        }

        static bool ExtractAchievementStatus(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("earned", out JsonElement earnedElement))
                return earnedElement.GetBoolean(); 
            else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
                return element.GetBoolean(); 
            return false;
        }

        static void LoadTranslator()
        {
            if (File.Exists(dictionaryPath))
            {
                try 
                {
                    string json = File.ReadAllText(dictionaryPath);
                    if (!string.IsNullOrWhiteSpace(json)) 
                        achievementNames = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch { achievementNames = new Dictionary<string, string>(); }
            }
        }

        public static void TriggerDataDownload(List<TrackedGame> games, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return;
            Task.Run(() => DownloadAllGameData(games, apiKey));
        }

        static async Task DownloadAllGameData(List<TrackedGame> games, string apiKey)
        {
            foreach (var game in games)
            {
                await FetchSteamData(game.AppId, apiKey);
            }
            File.WriteAllText(dictionaryPath, JsonSerializer.Serialize(achievementNames, new JsonSerializerOptions { WriteIndented = true }));
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
                    
                    if (doc.RootElement.TryGetProperty("game", out JsonElement gameElement) &&
                        gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) &&
                        statsElement.TryGetProperty("achievements", out JsonElement achievementsElement))
                    {
                        foreach (JsonElement ach in achievementsElement.EnumerateArray())
                        {
                            string internalName = ach.GetProperty("name").GetString() ?? "";
                            string displayName = ach.GetProperty("displayName").GetString() ?? internalName;
                            
                            achievementNames[$"{appId}_{internalName}"] = displayName;
                        }
                    }
                }
            }
            catch { }
        }

        static void ShowPopup(string achievementName)
        {
            Thread uiThread = new Thread(() =>
            {
                OverlayWindow window = new OverlayWindow(achievementName);
                Application.Run(window);
            });
            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.Start();
        }
    }
}