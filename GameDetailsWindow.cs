using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
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
            this.Size = new Size(850, 650);
            this.BackColor = steamBg;
            this.ForeColor = steamText;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable; 
            this.MaximizeBox = true; 

            if (Environment.OSVersion.Version.Major >= 10)
                DwmSetWindowAttribute(this.Handle, 20, new[] { 1 }, 4);

            InitializeUI();
            LoadAchievementsAsync();
        }

        private void InitializeUI()
        {
            // Top Banner Area
            Panel bannerPanel = new Panel { Size = new Size(850, 100), BackColor = steamPanel, Dock = DockStyle.Top };
            
            Label titleLabel = new Label
            {
                Text = game.Name, Font = new Font("Segoe UI", 24, FontStyle.Bold),
                Location = new Point(20, 20), AutoSize = true, ForeColor = Color.White
            };
            bannerPanel.Controls.Add(titleLabel);

            progressLabel = new Label
            {
                Text = "Calculating progress...", Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(24, 65), AutoSize = true, ForeColor = steamBlue
            };
            bannerPanel.Controls.Add(progressLabel);
            this.Controls.Add(bannerPanel);

            // Achievement List Area
            achievementListView = new ListView
            {
                Location = new Point(20, 120),
                Size = new Size(790, 470),
                BackColor = steamBg,
                ForeColor = steamText,
                Font = new Font("Segoe UI", 10),
                View = View.Details,
                FullRowSelect = true,
                
                // THIS HIDES THE UGLY WHITE BAR:
                HeaderStyle = ColumnHeaderStyle.None, 
                BorderStyle = BorderStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right 
            };

            // Set up columns with better default spacing
            achievementListView.Columns.Add("Achievement Name", 250);
            achievementListView.Columns.Add("Description", 400);
            achievementListView.Columns.Add("Status", 120);

            // DYNAMIC RESIZING: Make the description column stretch to fill the screen
            this.Resize += (s, e) => {
                if (achievementListView.Columns.Count >= 3) {
                    // Calculate remaining space and stretch the middle column
                    int remainingWidth = achievementListView.Width - achievementListView.Columns[0].Width - achievementListView.Columns[2].Width - 25;
                    achievementListView.Columns[1].Width = Math.Max(200, remainingWidth);
                }
            };

            achievementIcons.ImageSize = new Size(48, 48);
            achievementIcons.ColorDepth = ColorDepth.Depth32Bit;
            achievementListView.SmallImageList = achievementIcons;

            this.Controls.Add(achievementListView);
        }

        private async void LoadAchievementsAsync()
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return;
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

                        // Create temporary lists to sort them nicely without using buggy Windows Groups
                        List<ListViewItem> unlockedItems = new List<ListViewItem>();
                        List<ListViewItem> lockedItems = new List<ListViewItem>();
                        List<ListViewItem> hiddenItems = new List<ListViewItem>();

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
                            item.Font = new Font("Segoe UI", 11, FontStyle.Bold); // Make titles bolder
                            
                            if (isUnlocked)
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("✓ Unlocked");
                                item.ForeColor = steamBlue; 
                                unlockedItems.Add(item);
                            }
                            else if (isHidden)
                            {
                                item.SubItems.Add("Hidden Achievement - Keep playing to reveal!");
                                item.SubItems.Add("Locked");
                                item.ForeColor = Color.DimGray;
                                hiddenItems.Add(item);
                            }
                            else
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("Locked");
                                item.ForeColor = steamText;
                                lockedItems.Add(item);
                            }

                            string targetIconUrl = isUnlocked ? iconUrl : iconGrayUrl;
                            if (!string.IsNullOrEmpty(targetIconUrl))
                            {
                                string imageKey = internalName + (isUnlocked ? "_unlocked" : "_locked");
                                _ = LoadImageAsync(targetIconUrl, imageKey, item);
                            }
                        }
                        
                        // Add them to the list in order: Unlocked first, then locked, then hidden
                        achievementListView.Items.AddRange(unlockedItems.ToArray());
                        achievementListView.Items.AddRange(lockedItems.ToArray());
                        achievementListView.Items.AddRange(hiddenItems.ToArray());
                        
                        progressLabel.Text = $"{unlockedCount} / {totalCount} ACHIEVEMENTS EARNED";
                        
                        // Trigger a resize to fill the screen correctly right at launch
                        this.OnResize(EventArgs.Empty); 
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
                
                this.Invoke(new Action(() => {
                    if (!achievementIcons.Images.ContainsKey(key))
                    {
                        achievementIcons.Images.Add(key, img);
                    }
                    item.ImageKey = key;
                }));
            }
            catch { }
        }
    }
}