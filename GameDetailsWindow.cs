using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class GameDetailsWindow : Form
    {
        private TrackedGame game;
        private string apiKey;
        private ListView achievementListView;
        private HashSet<string> unlockedAchievements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient httpClient = new HttpClient();

        public GameDetailsWindow(TrackedGame game, string apiKey)
        {
            this.game = game;
            this.apiKey = apiKey;

            this.Text = $"{game.Name} - Achievement Progress";
            this.Size = new Size(700, 550);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            InitializeUI();
            LoadAchievementsAsync();
        }

        private void InitializeUI()
        {
            Label titleLabel = new Label
            {
                Text = $"{game.Name} Achievements",
                Font = new Font("Segoe UI", 16, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true,
                ForeColor = Color.DeepSkyBlue
            };
            this.Controls.Add(titleLabel);

            achievementListView = new ListView
            {
                Location = new Point(20, 60),
                Size = new Size(640, 420),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };

            achievementListView.Columns.Add("Achievement Name", 200);
            achievementListView.Columns.Add("Description", 300);
            achievementListView.Columns.Add("Status", 100);

            ListViewGroup grpUnlocked = new ListViewGroup("Unlocked Achievements");
            ListViewGroup grpLocked = new ListViewGroup("Locked Achievements");
            ListViewGroup grpHidden = new ListViewGroup("Hidden / Secret Achievements");

            achievementListView.Groups.Add(grpUnlocked);
            achievementListView.Groups.Add(grpLocked);
            achievementListView.Groups.Add(grpHidden);

            this.Controls.Add(achievementListView);
        }

        private async void LoadAchievementsAsync()
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Please save your Steam API Key in the dashboard first to view detailed achievements.", "API Key Missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.Close();
                return;
            }

            this.Text = "Loading data from Steam...";
            
            ScanLocalEmulatorSaves();
            await FetchAndPopulateSteamData();

            this.Text = $"{game.Name} - Achievement Progress";
        }

        private void ScanLocalEmulatorSaves()
        {
            string appId = game.AppId;
            string publicDocs = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

            string goldbergPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId, "achievements.json");
            ReadGoldbergState(goldbergPath);

            string[] emuFolders = { @"Steam\CODEX", @"Steam\RUNE", @"Steam\FLT", @"OnlineFix" };
            foreach (string emu in emuFolders)
            {
                string emuDir = Path.Combine(publicDocs, "Documents", emu, appId);
                if (Directory.Exists(emuDir))
                {
                    foreach (string file in Directory.GetFiles(emuDir, "*", SearchOption.AllDirectories))
                    {
                        // FIXED: Made this case-insensitive to catch "Achievements.ini"
                        string lowerFile = file.ToLower();
                        if (lowerFile.Contains("achievement") || lowerFile.Contains("stats"))
                            ReadIniState(file);
                    }
                }
            }
        }

        private void ReadGoldbergState(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path);
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    bool isEarned = false;
                    if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("earned", out JsonElement earned))
                        isEarned = earned.GetBoolean();
                    else if (prop.Value.ValueKind == JsonValueKind.True)
                        isEarned = true;

                    if (isEarned) unlockedAchievements.Add(prop.Name);
                }
            } catch { }
        }

        private void ReadIniState(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string[] lines = File.ReadAllLines(path);
                string currentSection = "";
                foreach (string line in lines)
                {
                    string originalLine = line.Trim();
                    string tLine = originalLine.ToLower(); // Compare in lowercase!

                    if (tLine.StartsWith("[") && tLine.EndsWith("]")) 
                    {
                        currentSection = originalLine.Substring(1, originalLine.Length - 2);
                    }
                    // FIXED: Now checks for achieved=true as well as achieved=1
                    else if ((tLine == "achieved=1" || tLine == "unlocked=1" || tLine.EndsWith("=1") || tLine == "achieved=true" || tLine == "unlocked=true" || tLine.EndsWith("=true")) && !string.IsNullOrEmpty(currentSection) && tLine != "steamachievements")
                    {
                        string achKey = originalLine.Contains("=") && tLine.Split('=')[0] != "achieved" && tLine.Split('=')[0] != "unlocked" ? originalLine.Split('=')[0] : currentSection;
                        unlockedAchievements.Add(achKey);
                    }
                }
            } catch { }
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
                        int unlockedCount = 0;
                        int totalCount = 0;

                        achievementListView.Items.Clear();

                        foreach (JsonElement ach in achievementsElement.EnumerateArray())
                        {
                            totalCount++;
                            string internalName = ach.GetProperty("name").GetString() ?? "";
                            string displayName = ach.GetProperty("displayName").GetString() ?? "Unknown";
                            
                            string description = "";
                            if (ach.TryGetProperty("description", out JsonElement descElement))
                                description = descElement.GetString() ?? "";

                            bool isHidden = false;
                            if (ach.TryGetProperty("hidden", out JsonElement hiddenElement))
                                isHidden = hiddenElement.GetInt32() == 1;

                            bool isUnlocked = unlockedAchievements.Contains(internalName);

                            if (isUnlocked) unlockedCount++;

                            ListViewItem item = new ListViewItem(displayName);
                            
                            if (isUnlocked)
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("✓ Unlocked");
                                item.ForeColor = Color.LightGreen;
                                item.Group = achievementListView.Groups[0]; 
                            }
                            else if (isHidden)
                            {
                                item.SubItems.Add("Hidden Achievement - Keep playing to reveal!");
                                item.SubItems.Add("Locked");
                                item.ForeColor = Color.Gray;
                                item.Group = achievementListView.Groups[2]; 
                            }
                            else
                            {
                                item.SubItems.Add(description);
                                item.SubItems.Add("Locked");
                                item.ForeColor = Color.LightGray;
                                item.Group = achievementListView.Groups[1]; 
                            }

                            achievementListView.Items.Add(item);
                        }
                        
                        this.Text = $"{game.Name} - Progress: {unlockedCount} / {totalCount} Achievements";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to Steam.\n\nError: {ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}