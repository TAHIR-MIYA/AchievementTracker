using System;
using System.Collections.Concurrent;
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
        static string dictionaryPath = AppPaths.InBase("achievement_dictionary.json");
        static string iconsPath = AppPaths.InBase("achievement_icons.json");
        
        static ConcurrentDictionary<string, string> achievementNames = new ConcurrentDictionary<string, string>();
        static ConcurrentDictionary<string, string> achievementIcons = new ConcurrentDictionary<string, string>();
        // ---- PROGRESS: how many of a game's achievements are unlocked (shown in the Library) ----
        public static event EventHandler? ProgressChanged;
        static readonly ConcurrentDictionary<string, (int Unlocked, int Total, long Tick)> progressCache = new ConcurrentDictionary<string, (int Unlocked, int Total, long Tick)>();

        static void NotifyProgressChanged()
        {
            progressCache.Clear();
            ProgressChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>False if the game's total isn't known yet (the names come from Steam and need your API key).</summary>
        public static bool GetProgress(string appId, out int unlocked, out int total)
        {
            long now = Environment.TickCount64;
            if (progressCache.TryGetValue(appId, out (int Unlocked, int Total, long Tick) cached) && now - cached.Tick < 1500)
            {
                unlocked = cached.Unlocked;
                total = cached.Total;
                return total > 0;
            }

            unlocked = 0;
            total = 0;
            try
            {
                string prefix = appId + "_";
                total = achievementNames.Keys.Count(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                unlocked = Math.Min(total, previousState.Count(entry => entry.Value && entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                unlocked = 0;
                total = 0;   // the name list was being updated at that moment; try again on the next paint
            }
            progressCache[appId] = (unlocked, total, now);
            return total > 0;
        }

        static DateTime suppressAlertsUntil = DateTime.MinValue;
        static bool AlertsSuppressed => DateTime.UtcNow < suppressAlertsUntil;

        // While files are being restored, unlocks found in them are remembered silently (no popups, no history).
        public static void SuppressAlertsFor(int seconds)
        {
            suppressAlertsUntil = DateTime.UtcNow.AddSeconds(seconds);
        }

        static string rarityPath = AppPaths.InBase("achievement_rarity.json");
        static ConcurrentDictionary<string, double> achievementRarity = new ConcurrentDictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        static ConcurrentDictionary<string, bool> previousState = new ConcurrentDictionary<string, bool>();   // watchers run on several threads

        static NotifyIcon? trayIcon;
        static Mutex? instanceMutex;            // held for the whole life of the app: only one copy may run
        static EventWaitHandle? showRequest;    // a second start sets this to ask the running copy to show its window
        static Control? uiMarshal;              // lets the listener thread reach the UI thread
        static MainWindow? dashboard;
        static List<FileSystemWatcher> activeWatchers = new List<FileSystemWatcher>();
        static readonly HttpClient httpClient = new HttpClient();

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // Only one copy at a time (it lives in the tray, so a second start would double every popup).
                instanceMutex = new Mutex(true, @"Local\AchievementTracker.SingleInstance", out bool firstInstance);
                showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AchievementTracker.ShowWindow");
                if (!firstInstance)
                {
                    showRequest.Set();   // ask the copy that is already running to show its window, then quit
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                uiMarshal = new Control();
                _ = uiMarshal.Handle;    // create the handle now, so other threads can use BeginInvoke later
                Thread showListener = new Thread(() =>
                {
                    while (true)
                    {
                        showRequest.WaitOne();
                        try { uiMarshal.BeginInvoke(new Action(() => ShowDashboard())); } catch { }
                    }
                })
                { IsBackground = true, Name = "ShowWindowListener" };
                showListener.Start();

                SetupSystemTray();
                LoadTranslator();
                StartPopupWorker();
                ApplyOverlaySettings();

                dashboard = new MainWindow();
                UpdateWatchers(dashboard.GetTrackedGames());

                TriggerDataDownload(dashboard.GetTrackedGames(), dashboard.SavedApiKey);
                StartupManager.RefreshPathIfEnabled();

                // Started at login (--minimized): stay in the tray and show the window only when asked.
                bool startHidden = args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
                if (startHidden) Application.Run(new ApplicationContext());
                else Application.Run(dashboard);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Achievement Tracker startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        static void SetupSystemTray()
        {
            trayIcon = new NotifyIcon()
            {
                Icon = AppPaths.LoadIcon() ?? SystemIcons.Application,
                ContextMenuStrip = new ContextMenuStrip(),
                Visible = true,
                Text = "Universal Achievement Tracker"
            };

            trayIcon.DoubleClick += (s, e) => ShowDashboard();

            ToolStripMenuItem startItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = StartupManager.IsEnabled() };
            startItem.Click += (s, e) =>
            {
                bool wanted = startItem.Checked;   // CheckOnClick has already flipped it
                if (!StartupManager.SetEnabled(wanted, out string error))
                {
                    startItem.Checked = !wanted;
                    MessageBox.Show(error, "Start with Windows", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            trayIcon.ContextMenuStrip.Opening += (s, e) => startItem.Checked = StartupManager.IsEnabled();

            trayIcon.ContextMenuStrip.Items.Add("Open Dashboard", null, (s, e) => ShowDashboard());
            trayIcon.ContextMenuStrip.Items.Add("Open Activity", null, (s, e) => ShowDashboard(true));
            trayIcon.ContextMenuStrip.Items.Add("Test popup", null, (s, e) => ShowTestPopup());
            trayIcon.ContextMenuStrip.Items.Add("Test milestone popup", null, (s, e) => ShowTestMilestone());
            trayIcon.ContextMenuStrip.Items.Add("-");
            trayIcon.ContextMenuStrip.Items.Add(startItem);
            trayIcon.ContextMenuStrip.Items.Add("-");
            trayIcon.ContextMenuStrip.Items.Add("Exit Tracker", null, (s, e) =>
            {
                if (trayIcon != null) trayIcon.Visible = false;
                OverlayServer.Stop();
                Environment.Exit(0);
            });
        }

        // Brings the window to the front (it may be hidden in the tray, or was never shown when started at login).
        public static void ShowDashboard(bool activityPage = false)
        {
            if (dashboard == null) return;
            dashboard.Show();
            dashboard.WindowState = FormWindowState.Normal;
            dashboard.Activate();
            if (activityPage) dashboard.ShowActivityPage();
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
                string appId = game.AppId.Trim();
                if (!uint.TryParse(appId, out _)) continue;
                game.AppId = appId;

                try
                {
                // 1. WATCH GOLDBERG
                string goldbergDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId);
                Directory.CreateDirectory(goldbergDir); 
                LoadBaselineState(appId, Path.Combine(goldbergDir, "achievements.json"));
                AttachWatcher(goldbergDir, "*.json", (s, e) => OnAchievementFileChanged(s, e, appId));

                // 2. WATCH ALL INI EMULATORS (CODEX, RUNE, FLT, TENOKE, OnlineFix)
                string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"Steam\TENOKE", @"OnlineFix" };
                
                foreach (string emu in emuFolders)
                {
                    string emuDir = Path.Combine(publicDocs, "Documents", emu, appId);
                    Directory.CreateDirectory(emuDir);

                    // Pre-scan directories to set baseline
                    string iniPath = Path.Combine(emuDir, "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "remote", "achievements.ini");
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "achievements"); 
                    if (!File.Exists(iniPath)) iniPath = Path.Combine(emuDir, "Stats", "Achievements"); 
                    
                    ParseIniForBaseline(appId, iniPath);

                    // Watch the whole directory including subfolders to catch everything
                    AttachWatcher(emuDir, "*.*", (s, e) => {
                        string lowerName = e.Name?.ToLower() ?? "";
                        if (lowerName.Contains("achievement") || lowerName.Contains("stats")) 
                            ParseIniForChanges(appId, e.FullPath);
                    });
                }
                }
                catch
                {
                    // One broken game entry (locked folder, no permission...) must never stop the app from starting.
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

        static void LoadBaselineState(string appId, string filePath)
        {
            if (!File.Exists(filePath)) return;
            try
            {
                // Basic retry logic for file locks
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
                        if (!AlertsSuppressed) TriggerUI(appId, achKey);
                    }
                    else if (!isEarned && previousState.ContainsKey(stateKey))
                    {
                        // Delete from memory so you can re-test it using Notepad!
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
                foreach (var entry in AchievementIni.Read(filePath))
                {
                    if (entry.Value) previousState[appId + "_" + entry.Key] = true;
                }
            }
            catch { }
        }

        static void ParseIniForChanges(string appId, string filePath)
        {
            Thread.Sleep(50);
            try
            {
                foreach (var entry in AchievementIni.Read(filePath))
                {
                    string stateKey = appId + "_" + entry.Key;
                    if (entry.Value)
                    {
                        if (!previousState.TryGetValue(stateKey, out bool wasUnlocked) || !wasUnlocked)
                        {
                            previousState[stateKey] = true;
                            if (!AlertsSuppressed) TriggerUI(appId, entry.Key);
                        }
                    }
                    else
                    {
                        // Forget it, so you can re-test by editing the file in Notepad.
                        previousState[stateKey] = false;
                    }
                }
            }
            catch { }
        }

        static void TriggerUI(string appId, string achKey)
        {
            string lookupKey = appId + "_" + achKey;
            string displayName = achievementNames.ContainsKey(lookupKey) ? achievementNames[lookupKey] : achKey;
            string iconUrl = achievementIcons.ContainsKey(lookupKey) ? achievementIcons[lookupKey] : "";
            double? percent = achievementRarity.TryGetValue(lookupKey, out double p) ? p : (double?)null;

            RecordUnlock(appId, achKey, displayName, percent);
            ShowPopup(displayName, iconUrl, percent);
            CheckMilestones(appId);
            NotifyProgressChanged();
        }

        static string GetGameName(string appId)
        {
            try
            {
                TrackedGame? tracked = dashboard?.GetTrackedGames().FirstOrDefault(g => g.AppId == appId);
                if (tracked != null && tracked.Name.Length > 0) return tracked.Name;
            }
            catch { }
            return "App " + appId;
        }

        // Shows a special popup when this unlock takes a game past 25, 50, 75 or 100 percent of its achievements.
        static void CheckMilestones(string appId)
        {
            if (!AppSettings.Current.MilestonePopups) return;
            try
            {
                string prefix = appId + "_";
                int total = achievementNames.Keys.Count(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (total < 4) return;   // the names come from Steam (needs your API key); with so few achievements percentages mean little

                int unlocked = previousState.Count(entry => entry.Value && entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                unlocked = Math.Min(unlocked, total);
                if (unlocked <= 0) return;
                int before = unlocked - 1;

                // the highest milestone this single unlock crossed (integer maths, no rounding surprises)
                int reached = 0;
                foreach (int step in new[] { 25, 50, 75, 100 })
                {
                    if (unlocked * 100 >= step * total && before * 100 < step * total) reached = step;
                }
                if (reached == 0) return;

                string game = GetGameName(appId);
                bool completed = reached == 100;
                ShowPopup(new PopupData
                {
                    Name = completed ? "All achievements in " + game : game + ": " + reached + "% complete",
                    Header = completed ? "Game completed" : "Milestone reached",
                    RightText = unlocked + " of " + total,
                    Highlight = true,
                    CoverAppId = appId
                });
            }
            catch { }
        }

        // Saves the unlock to the history (and takes a screenshot if enabled) without slowing the popup down.
        static void RecordUnlock(string appId, string achKey, string displayName, double? percent)
        {
            string gameName = "App " + appId;
            try
            {
                TrackedGame? tracked = dashboard?.GetTrackedGames().FirstOrDefault(g => g.AppId == appId);
                if (tracked != null && tracked.Name.Length > 0) gameName = tracked.Name;
            }
            catch { }

            bool takeScreenshot = AppSettings.Current.ScreenshotOnUnlock;
            DateTime now = DateTime.UtcNow;
            Task.Run(() =>
            {
                string? screenshot = takeScreenshot ? ScreenshotService.Capture(gameName, displayName) : null;
                UnlockHistory.Add(new UnlockEntry
                {
                    TimeUtc = now,
                    AppId = appId,
                    GameName = gameName,
                    Key = achKey,
                    Name = displayName,
                    Percent = percent,
                    Screenshot = screenshot
                });
                BackupService.ScheduleAfterUnlock();
            });
        }

        // The folders where emulators keep the achievement files of a game (only those that exist).
        public static List<(string Label, string Dir)> GetAchievementFolders(string appId)
        {
            List<(string Label, string Dir)> folders = new List<(string Label, string Dir)>();
            string publicDir = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

            string goldberg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId);
            if (Directory.Exists(goldberg)) folders.Add(("Goldberg", goldberg));

            (string Label, string Sub)[] emulators =
            {
                ("CODEX", @"Steam\CODEX"), ("RUNE", @"Steam\RUNE"), ("FLT", @"Steam\FLT"),
                ("TENOKE", @"Steam\TENOKE"), ("OnlineFix", "OnlineFix")
            };
            foreach (var emulator in emulators)
            {
                string dir = Path.Combine(publicDir, "Documents", emulator.Sub, appId);
                if (Directory.Exists(dir)) folders.Add((emulator.Label, dir));
            }
            return folders;
        }

        public static List<TrackedGame> GetTrackedGamesSnapshot()
        {
            try { return dashboard?.GetTrackedGames().ToList() ?? new List<TrackedGame>(); }
            catch { return new List<TrackedGame>(); }
        }

        static void LoadTranslator()
        {
            if (File.Exists(dictionaryPath)) {
                try {
                    string json = File.ReadAllText(dictionaryPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null) foreach (var kvp in dict) achievementNames.TryAdd(kvp.Key, kvp.Value);
                    }
                } catch { }
            }

            if (File.Exists(iconsPath)) {
                try {
                    string json = File.ReadAllText(iconsPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict != null) foreach (var kvp in dict) achievementIcons.TryAdd(kvp.Key, kvp.Value);
                    }
                } catch { }
            }

            if (File.Exists(rarityPath))
            {
                try
                {
                    var saved = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(rarityPath));
                    if (saved != null) foreach (var entry in saved) achievementRarity[entry.Key] = entry.Value;
                }
                catch { }
            }
        }

        public static void TriggerDataDownload(List<TrackedGame> games, string apiKey)
        {
            List<TrackedGame> snapshot = games.ToList();   // the list can change while we download
            Task.Run(() => DownloadAllGameData(snapshot, apiKey));
        }

        static async Task DownloadAllGameData(List<TrackedGame> games, string apiKey)
        {
            bool hasKey = !string.IsNullOrWhiteSpace(apiKey);

            foreach (var game in games)
            {
                if (hasKey) await FetchSteamData(game.AppId, apiKey);
                await FetchRarity(game.AppId);   // public data, no key needed
            }

            try
            {
                if (hasKey)
                {
                    File.WriteAllText(dictionaryPath, JsonSerializer.Serialize(achievementNames, new JsonSerializerOptions { WriteIndented = true }));
                    File.WriteAllText(iconsPath, JsonSerializer.Serialize(achievementIcons, new JsonSerializerOptions { WriteIndented = true }));
                }
                SaveRarity();
            }
            catch { }
            NotifyProgressChanged();
        }

        // ---- RARITY: share of all Steam players who unlocked each achievement ----
        static async Task FetchRarity(string appId)
        {
            try
            {
                string url = $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v2/?gameid={appId}";
                using (HttpResponseMessage response = await httpClient.GetAsync(url))
                {
                    if (!response.IsSuccessStatusCode) return;
                    using (JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
                    {
                        if (!doc.RootElement.TryGetProperty("achievementpercentages", out JsonElement container) ||
                            !container.TryGetProperty("achievements", out JsonElement list)) return;

                        foreach (JsonElement item in list.EnumerateArray())
                        {
                            string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                            if (name.Length == 0 || !item.TryGetProperty("percent", out JsonElement p)) continue;

                            double percent = -1;
                            if (p.ValueKind == JsonValueKind.Number) percent = p.GetDouble();
                            else if (p.ValueKind == JsonValueKind.String &&
                                     double.TryParse(p.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) percent = parsed;

                            if (percent >= 0) achievementRarity[appId + "_" + name] = percent;
                        }
                    }
                }
            }
            catch { }
        }

        static void SaveRarity()
        {
            File.WriteAllText(rarityPath, JsonSerializer.Serialize(achievementRarity.ToDictionary(e => e.Key, e => e.Value), new JsonSerializerOptions { WriteIndented = true }));
        }

        public static bool TryGetRarity(string appId, string achievementName, out double percent)
        {
            return achievementRarity.TryGetValue(appId + "_" + achievementName, out percent);
        }

        // Used by the details window: fetches the numbers once if this game isn't cached yet.
        public static async Task EnsureRarityAsync(string appId)
        {
            string prefix = appId + "_";
            if (achievementRarity.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return;
            await FetchRarity(appId);
            try { SaveRarity(); } catch { }
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

        // ---- POPUPS ----
        // One worker thread shows popups one after another, so several unlocks at once
        // no longer pile up on top of each other (the old code started a new thread per popup).
        static readonly BlockingCollection<PopupData> popupQueue = new BlockingCollection<PopupData>();

        static void StartPopupWorker()
        {
            Thread worker = new Thread(() =>
            {
                foreach (var item in popupQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        using OverlayWindow window = new OverlayWindow(item);
                        Application.Run(window);
                    }
                    catch { }
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Name = "PopupWorker";
            worker.Start();
        }

        static void ShowPopup(PopupData data)
        {
            AppSettings settings = AppSettings.Current;
            if (settings.ObsOverlay) OverlayServer.Broadcast(data);   // browser source (OBS)
            if (settings.DesktopPopup) popupQueue.Add(data);          // on-screen popup
        }

        static void ShowPopup(string achievementName, string iconUrl, double? percent = null)
        {
            ShowPopup(new PopupData { Name = achievementName, Icon = iconUrl, Percent = percent });
        }

        public static void ShowTestPopup() => ShowPopup("Test Achievement", "", 1.8);

        public static void ShowTestMilestone() => ShowPopup(new PopupData
        {
            Name = "Test Game: 50% complete",
            Header = "Milestone reached",
            RightText = "12 of 24",
            Highlight = true
        });

        // Starts or stops the local OBS overlay server according to the current settings.
        public static void ApplyOverlaySettings()
        {
            AppSettings settings = AppSettings.Current;
            if (settings.ObsOverlay)
            {
                if (!OverlayServer.IsRunning) OverlayServer.Start(settings.ObsPort);
            }
            else
            {
                OverlayServer.Stop();
            }
        }
    }
}
