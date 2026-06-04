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
        
        // 🛑 PASTE YOUR STEAM API KEY INSIDE THE QUOTES BELOW!
        static string steamApiKey = "PASTE_YOUR_KEY_HERE"; 

        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SetupSystemTray();
            LoadTranslator();

            dashboard = new MainWindow();
            UpdateWatchers(dashboard.GetTrackedGames());
            
            Task.Run(() => DownloadAllGameData(dashboard.GetTrackedGames()));

            Application.Run(dashboard); // Run the Dashboard UI
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
                
                // Track baseline state so we don't popup old achievements on startup
                LoadBaselineState(game.AppId, Path.Combine(goldbergDir, "achievements.json"));

                FileSystemWatcher watcher = new FileSystemWatcher(goldbergDir, "achievements.json");
                watcher.NotifyFilter = NotifyFilters.LastWrite;
                watcher.Changed += (sender, e) => OnAchievementFileChanged(sender, e, game.AppId);
                watcher.EnableRaisingEvents = true;
                activeWatchers.Add(watcher);
            }
        }

        static void LoadBaselineState(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                string json = File.ReadAllText(filePath);
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    bool isEarned = ExtractAchievementStatus(prop.Value);
                    if (isEarned) previousState[appId + "_" + prop.Name] = true;
                }
            }
            catch { }
        }

        static void OnAchievementFileChanged(object sender, FileSystemEventArgs e, string appId)
        {
            Thread.Sleep(50); // Wait for the game engine to release the file lock
            try
            {
                using var fs = new FileStream(e.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string json = sr.ReadToEnd();

                using JsonDocument doc = JsonDocument.Parse(json);
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

        // 🧠 NEW: Can read both our fake files AND real complex Goldberg files
        static bool ExtractAchievementStatus(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("earned", out JsonElement earnedElement))
            {
                return earnedElement.GetBoolean(); // Real Goldberg format
            }
            else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
            {
                return element.GetBoolean(); // Our simple mock format
            }
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

        static async Task DownloadAllGameData(List<TrackedGame> games)
        {
            foreach (var game in games)
            {
                await FetchSteamData(game.AppId);
            }
        }

        // 🌐 NEW: Connects to the real Steam API
        static async Task FetchSteamData(string appId)
        {
            if (string.IsNullOrEmpty(steamApiKey) || steamApiKey == "PASTE_YOUR_KEY_HERE")
            {
                // Fallback if you haven't put your key in yet
                achievementNames[appId + "_FIRST_BLOOD"] = "Slayer of Demons (Mock API)";
                return;
            }

            try
            {
                string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={steamApiKey}&appid={appId}";
                HttpResponseMessage response = await httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    
                    // Parse the massive Steam JSON payload
                    using JsonDocument doc = JsonDocument.Parse(jsonResponse);
                    if (doc.RootElement.TryGetProperty("game", out JsonElement gameElement) &&
                        gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) &&
                        statsElement.TryGetProperty("achievements", out JsonElement achievementsElement))
                    {
                        foreach (JsonElement ach in achievementsElement.EnumerateArray())
                        {
                            if (ach.TryGetProperty("name", out JsonElement nameElement) && 
                                ach.TryGetProperty("displayName", out JsonElement displayNameElement))
                            {
                                string internalName = nameElement.GetString() ?? "";
                                string displayName = displayNameElement.GetString() ?? internalName;
                                
                                achievementNames[appId + "_" + internalName] = displayName;
                            }
                        }
                    }
                }
            }
            catch { /* Ignore network errors */ }

            // Save the newly downloaded real names to the hard drive
            File.WriteAllText(dictionaryPath, JsonSerializer.Serialize(achievementNames, new JsonSerializerOptions { WriteIndented = true }));
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