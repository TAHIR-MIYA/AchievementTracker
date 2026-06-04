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
            trayIcon.ContextMenuStrip.Items.Add("Exit Tracker", null, (s, e) => { if (trayIcon != null) trayIcon.Visible = false; Environment.Exit(0); });
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
                // 1. WATCH GOLDBERG (JSON)
                string goldbergDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", game.AppId);
                Directory.CreateDirectory(goldbergDir); 
                LoadGoldbergBaseline(game.AppId, Path.Combine(goldbergDir, "achievements.json"));
                AttachWatcher(goldbergDir, "*.json", (s, e) => OnGoldbergFileChanged(s, e, game.AppId));

                // 2. WATCH PUBLIC DOCUMENT EMULATORS (INI)
                // This covers CODEX, RUNE, FLT, and OnlineFix!
                string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"OnlineFix" };
                
                foreach (string emu in emuFolders)
                {
                    string emuDir = Path.Combine(publicDocs, "Documents", emu, game.AppId);
                    Directory.CreateDirectory(emuDir);

                    // Load baseline state so we don't popup old achievements
                    string iniPath = Path.Combine(emuDir, "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "remote", "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "achievements"); // Old CODEX style
                    ParseIni(game.AppId, iniPath, isBaseline: true);

                    AttachWatcher(emuDir, "*", (s, e) => {
                        if (e.Name.Contains("achievement") || e.Name.Contains("stats")) 
                            ParseIni(game.AppId, e.FullPath, isBaseline: false);
                    });
                }
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

        // --- GOLDBERG JSON PARSER ---
        static void LoadGoldbergBaseline(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                using JsonDocument doc = JsonDocument.Parse(sr.ReadToEnd());
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (ExtractGoldbergStatus(prop.Value)) previousState[appId + "_" + prop.Name] = true;
                }
            } catch { }
        }

        static void OnGoldbergFileChanged(object sender, FileSystemEventArgs e, string appId)
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
                    bool isEarned = ExtractGoldbergStatus(prop.Value);
                    string stateKey = appId + "_" + achKey; 

                    if (isEarned && (!previousState.ContainsKey(stateKey) || !previousState[stateKey]))
                    {
                        previousState[stateKey] = true; 
                        ShowPopup(GetDisplayName(appId, achKey));
                    }
                }
            } catch { }
        }

        static bool ExtractGoldbergStatus(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("earned", out JsonElement earned)) return earned.GetBoolean(); 
            else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False) return element.GetBoolean(); 
            return false;
        }

        // --- UNIVERSAL INI PARSER (CODEX, RUNE, FLT, OnlineFix) ---
        static void ParseIni(string appId, string filePath, bool isBaseline)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                Thread.Sleep(50); // Prevent file locks
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                string line;
                string currentSection = "";

                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("[") && line.EndsWith("]")) currentSection = line.Substring(1, line.Length - 2);
                    else if ((line == "Achieved=1" || line == "Unlocked=1" || line.EndsWith("=1")) && !string.IsNullOrEmpty(currentSection) && currentSection != "SteamAchievements")
                    {
                        // Some emulators put the achievement code as the section, some put it as the key. We handle both.
                        string achKey = line.Contains("=") && line.Split('=')[0] != "Achieved" && line.Split('=')[0] != "Unlocked" ? line.Split('=')[0] : currentSection;
                        string stateKey = appId + "_" + achKey; 

                        if (!previousState.ContainsKey(stateKey) || !previousState[stateKey])
                        {
                            previousState[stateKey] = true; 
                            if (!isBaseline) ShowPopup(GetDisplayName(appId, achKey));
                        }
                    }
                }
            } catch { }
        }

        // --- API & DISPLAY LOGIC ---
        static string GetDisplayName(string appId, string internalKey)
        {
            string lookupKey = appId + "_" + internalKey;
            return achievementNames.ContainsKey(lookupKey) ? achievementNames[lookupKey] : internalKey;
        }

        static void LoadTranslator()
        {
            if (File.Exists(dictionaryPath))
            {
                try {
                    string json = File.ReadAllText(dictionaryPath);
                    if (!string.IsNullOrWhiteSpace(json)) achievementNames = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                } catch { achievementNames = new Dictionary<string, string>(); }
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
                            achievementNames[$"{appId}_{internalName}"] = ach.GetProperty("displayName").GetString() ?? internalName;
                        }
                    }
                }
            } catch { }
        }

        static void ShowPopup(string achievementName)
        {
            if (dashboard != null)
            {
                dashboard.Invoke(new Action(() => {
                    OverlayWindow window = new OverlayWindow(achievementName);
                    window.Show(); 
                }));
            }
        }
    }
}