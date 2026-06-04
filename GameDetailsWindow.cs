using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class GameDetailsWindow : Form
    {
        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);

        private TrackedGame game;
        private string apiKey;
        private ListView achievementListView = new ListView();
        private HashSet<string> unlockedAchievements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient httpClient = new HttpClient();
        private Label progressLabel = new Label();
        private ImageList achievementIcons = new ImageList();

        // Steam Palette
        private Color steamBg = ColorTranslator.FromHtml("#1b2838");
        private Color steamPanel = ColorTranslator.FromHtml("#171a21");
        private Color steamText = ColorTranslator.FromHtml("#c7d5e0");
        private Color steamBlue = ColorTranslator.FromHtml("#66c0f4");

        public GameDetailsWindow(TrackedGame game, string apiKey)
        {
            this.game = game;
            this.apiKey = apiKey;

            this.Text = $"{game.Name} - Activity";
            this.Size = new Size(750, 600);
            this.BackColor = steamBg;
            this.ForeColor = steamText;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            if (Environment.OSVersion.Version.Major >= 10)
                DwmSetWindowAttribute(this.Handle, 20, new[] { 1 }, 4);

            InitializeUI();
            LoadAchievementsAsync();
        }

        private void InitializeUI()
        {
            // Top Banner Area
            Panel bannerPanel = new Panel { Size = new Size(750, 100), BackColor = steamPanel, Location = new Point(0, 0) };
            
            Label titleLabel = new Label
            {
                Text = game.Name, Font = new Font("Segoe UI", 24, FontStyle.Bold),
                Location = new Point(20, 20), AutoSize = true, ForeColor = Color.White
            };
            bannerPanel.Controls.Add(titleLabel);

            progressLabel = new Label
            {
                Text = "Calculating progress...", Font = new Font("Segoe UI", 10),
                Location = new Point(24, 65), AutoSize = true, ForeColor = steamBlue
            };
            bannerPanel.Controls.Add(progressLabel);
            this.Controls.Add(bannerPanel);

            // Achievement List Area
            achievementListView = new ListView
            {
                Location = new Point(20, 120),
                Size = new Size(690, 420),
                BackColor = steamBg,
                ForeColor = steamText,
                Font = new Font("Segoe UI", 10),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BorderStyle = BorderStyle.None
            };

            achievementListView.Columns.Add("Achievement Name", 220);
            achievementListView.Columns.Add("Description", 350);
            achievementListView.Columns.Add("Status", 100);

            // Set up image list for icons
            achievementIcons.ImageSize = new Size(48, 48);
            achievementIcons.ColorDepth = ColorDepth.Depth32Bit;
            achievementListView.SmallImageList = achievementIcons;

            // Set up Steam-style groups
            ListViewGroup grpUnlocked = new ListViewGroup("Unlocked Achievements");
            ListViewGroup grpLocked = new ListViewGroup("Locked Achievements");
            ListViewGroup grpHidden = new ListViewGroup("Hidden Achievements");

            achievementListView.Groups.Add(grpUnlocked);
            achievementListView.Groups.Add(grpLocked);
            achievementListView.Groups.Add(grpHidden);

            this.Controls.Add(achievementListView);
        }

        private async void LoadAchievementsAsync()
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please save your Steam API Key in the dashboard to view achievements.", "API Key Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.Close(); return;
            }

            ScanLocalEmulatorSaves();
            await FetchAndPopulateSteamData();
        }

        private void ScanLocalEmulatorSaves()
        {
            string appId = game.AppId;
            string publicDocs = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

            // 1. GOLDBERG
            string goldbergPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId, "achievements.json");
            if (File.Exists(goldbergPath))
            {
                try {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(goldbergPath));
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("earned", out JsonElement earned) && earned.GetBoolean())
                            unlockedAchievements.Add(prop.Name);
                        else if (prop.Value.ValueKind == JsonValueKind.True) unlockedAchievements.Add(prop.Name);
                    }
                } catch { }
            }

            // 2. CODEX, RUNE, FLT, TENOKE, OnlineFix
            string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"Steam\TENOKE", @"OnlineFix" };
            foreach (string emu in emuFolders)
            {
                string emuDir = Path.Combine(publicDocs, "Documents", emu, appId);
                if (Directory.Exists(emuDir))
                {
                    foreach (string file in Directory.GetFiles(emuDir, "*", SearchOption.AllDirectories))
                    {
                        string lowerFile = file.ToLower();
                        if (lowerFile.Contains("achievement") || lowerFile.Contains("stats"))
                        {
                            try {
                                string[] lines = File.ReadAllLines(file);
                                string currentSection = "";
                                foreach (string line in lines)
                                {
                                    string originalLine = line.Trim();
                                    string tLine = originalLine.ToLower(); 

                                    if (tLine.StartsWith("[") && tLine.EndsWith("]")) 
                                        currentSection = originalLine.Substring(1, originalLine.Length - 2);
                                    else if ((tLine == "achieved=1" || tLine.EndsWith("=1") || tLine == "achieved=true" || tLine.EndsWith("=true")) && !string.IsNullOrEmpty(currentSection) && tLine != "steamachievements")
                                    {
                                        string achKey = originalLine.Contains("=") && tLine.Split('=')[0] != "achieved" ? originalLine.Split('=')[0] : currentSection;
                                        unlockedAchievements.Add(achKey);
                                    }
                                }
                            } catch { }
                        }
                    }
                }
            }
        }

        private async Task FetchAndPopulateSteamData()
        {
            try
            {
                string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={game.AppId}";
                HttpResponseMessage response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("game", out JsonElement gameElement) &&
                        gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) &&
                        statsElement.TryGetProperty("achievements", out JsonElement achievementsElement))
                    {
                        int unlockedCount = 0; int totalCount = 0;
                        achievementListView.Items.Clear();

                        foreach (JsonElement ach in achievementsElement.EnumerateArray())
                        {
                            totalCount++;
                            string internalName = ach.GetProperty("name").GetString() ?? "";
                            string displayName = ach.GetProperty("displayName").GetString() ?? "Unknown";
                            string description = ach.TryGetProperty("description", out JsonElement descElement) ? descElement.GetString() ?? "" : "";
                            bool isHidden = ach.TryGetProperty("hidden", out JsonElement hiddenElement) && hiddenElement.GetInt32() == 1;
                            
                            string iconUrl = ach.TryGetProperty("icon", out JsonElement iconEl) ? iconEl.GetString() ?? "" : "";
                            string iconGrayUrl = ach.TryGetProperty("icongray", out JsonElement iconGrayEl) ? iconGrayEl.GetString() ?? "" : "";

                            bool isUnlocked = unlockedAchievements.Contains(internalName);
                            if (isUnlocked) unlockedCount++;

                            ListViewItem item = new ListViewItem(displayName);
                            
                            if (isUnlocked)
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("✓ Unlocked");
                                item.ForeColor = steamBlue; 
                                item.Group = achievementListView.Groups[0]; 
                            }
                            else if (isHidden)
                            {
                                item.SubItems.Add("Hidden Achievement - Keep playing to reveal!");
                                item.SubItems.Add("Locked");
                                item.ForeColor = Color.DimGray;
                                item.Group = achievementListView.Groups[2]; 
                            }
                            else
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("Locked");
                                item.ForeColor = Color.Gray;
                                item.Group = achievementListView.Groups[1]; 
                            }

                            achievementListView.Items.Add(item);

                            // Trigger async image download
                            string targetIconUrl = isUnlocked ? iconUrl : iconGrayUrl;
                            if (!string.IsNullOrEmpty(targetIconUrl))
                            {
                                string imageKey = internalName + (isUnlocked ? "_unlocked" : "_locked");
                                _ = LoadImageAsync(targetIconUrl, imageKey, item);
                            }
                        }
                        
                        progressLabel.Text = $"{unlockedCount} / {totalCount} ACHIEVEMENTS EARNED";
                    }
                }
            }
            catch { MessageBox.Show("Failed to connect to Steam.", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private async Task LoadImageAsync(string url, string key, ListViewItem item)
        {
            try
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                using MemoryStream ms = new MemoryStream(imageBytes);
                Image img = Image.FromStream(ms);
                
                // We must update UI elements on the main UI thread
                this.Invoke(new Action(() => {
                    if (!achievementIcons.Images.ContainsKey(key))
                    {
                        achievementIcons.Images.Add(key, img);
                    }
                    item.ImageKey = key;
                }));
            }
            catch { } // Silently ignore failed image downloads
        }
    }
}