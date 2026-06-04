using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class TrackedGame
    {
        public string Name { get; set; } = "";
        public string AppId { get; set; } = "";
    }

    public class MainWindow : Form
    {
        private ListBox gameList = new ListBox();
        private TextBox nameInput = new TextBox();
        private TextBox appIdInput = new TextBox();
        private TextBox apiInput = new TextBox(); 
        
        private List<TrackedGame> games = new List<TrackedGame>();
        private string gamesFilePath = "tracked_games.json";
        private string settingsFilePath = "app_settings.json"; 

        public string SavedApiKey { get; private set; } = "";

        public MainWindow()
        {
            this.Text = "Universal Achievement Tracker";
            this.Size = new Size(500, 420); 
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            LoadGames();
            LoadSettings(); 
            InitializeUI();
        }

        private void InitializeUI()
        {
            Label listLabel = new Label { Text = "Tracked Games:", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            this.Controls.Add(listLabel);

            gameList = new ListBox
            {
                Location = new Point(20, 45),
                Size = new Size(200, 240),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            RefreshGameListUI();
            this.Controls.Add(gameList);

            Label addLabel = new Label { Text = "Add New Game", Location = new Point(240, 20), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            this.Controls.Add(addLabel);

            Label nameLabel = new Label { Text = "Game Name:", Location = new Point(240, 60), AutoSize = true };
            this.Controls.Add(nameLabel);
            nameInput = new TextBox { Location = new Point(240, 80), Size = new Size(200, 25), BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(nameInput);

            Label appIdLabel = new Label { Text = "Steam App ID (e.g. 1091500):", Location = new Point(240, 115), AutoSize = true };
            this.Controls.Add(appIdLabel);
            appIdInput = new TextBox { Location = new Point(240, 135), Size = new Size(200, 25), BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(appIdInput);

            Button addButton = new Button
            {
                Text = "Add to Tracker",
                Location = new Point(240, 180),
                Size = new Size(200, 35),
                BackColor = Color.DeepSkyBlue,
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            addButton.FlatAppearance.BorderSize = 0;
            addButton.Click += AddButton_Click;
            this.Controls.Add(addButton);

            Button autoDetectBtn = new Button
            {
                Text = "🔍 Auto-Detect (Browse .exe)",
                Location = new Point(240, 225),
                Size = new Size(200, 30),
                BackColor = Color.SlateBlue,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9)
            };
            autoDetectBtn.FlatAppearance.BorderSize = 0;
            autoDetectBtn.Click += AutoDetectBtn_Click;
            this.Controls.Add(autoDetectBtn);

            Panel divider = new Panel { Size = new Size(440, 1), BackColor = Color.Gray, Location = new Point(20, 300) };
            this.Controls.Add(divider);

            Label apiLabel = new Label { Text = "Steam API Key (For real achievement names):", Location = new Point(20, 315), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            this.Controls.Add(apiLabel);
            
            apiInput = new TextBox { 
                Location = new Point(20, 335), 
                Size = new Size(300, 25), 
                BackColor = Color.FromArgb(60, 60, 60), 
                ForeColor = Color.White, 
                BorderStyle = BorderStyle.FixedSingle,
                Text = SavedApiKey 
            };
            this.Controls.Add(apiInput);

            Button saveApiButton = new Button
            {
                Text = "Save Key",
                Location = new Point(330, 334),
                Size = new Size(110, 27),
                BackColor = Color.MediumSeaGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveApiButton.FlatAppearance.BorderSize = 0;
            saveApiButton.Click += SaveApiButton_Click;
            this.Controls.Add(saveApiButton);
        }

        private void AutoDetectBtn_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Game Executable (*.exe)|*.exe";
                openFileDialog.Title = "Select the cracked game's main .exe file";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string dir = Path.GetDirectoryName(openFileDialog.FileName) ?? "";
                    string detectedAppId = "";

                    // 1. Check standard text files
                    string[] txtFiles = { "steam_appid.txt", @"steam_settings\steam_appid.txt" };
                    foreach (var txt in txtFiles)
                    {
                        string path = Path.Combine(dir, txt);
                        if (File.Exists(path)) { detectedAppId = File.ReadAllText(path).Trim(); break; }
                    }

                    // 2. Check ALL known emulator config files
                    if (string.IsNullOrEmpty(detectedAppId))
                    {
                        string[] iniFiles = { "steam_emu.ini", "OnlineFix.ini", "FLT.ini", "SmartSteamEmu.ini", "tenoke.ini", "ali213.ini" };
                        foreach (var ini in iniFiles)
                        {
                            string path = Path.Combine(dir, ini);
                            if (File.Exists(path))
                            {
                                foreach (string line in File.ReadLines(path))
                                {
                                    if (line.Trim().StartsWith("AppId=") || line.Trim().StartsWith("RealAppId=") || line.Trim().StartsWith("AppID="))
                                    {
                                        detectedAppId = line.Split('=')[1].Trim();
                                        break;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(detectedAppId)) break;
                        }
                    }

                    if (!string.IsNullOrEmpty(detectedAppId))
                    {
                        appIdInput.Text = detectedAppId;
                        nameInput.Text = new DirectoryInfo(dir).Name;
                    }
                    else
                    {
                        MessageBox.Show("Could not automatically find the Steam App ID. You may need to look it up on SteamDB.info and enter it manually.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SaveApiButton_Click(object? sender, EventArgs e)
        {
            SavedApiKey = apiInput.Text.Trim();
            File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(new { SteamApiKey = SavedApiKey }));
            MessageBox.Show("API Key Saved Successfully!", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Program.TriggerDataDownload(games, SavedApiKey);
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsFilePath))
            {
                try {
                    string json = File.ReadAllText(settingsFilePath);
                    using JsonDocument doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("SteamApiKey", out JsonElement keyElement))
                        SavedApiKey = keyElement.GetString() ?? "";
                } catch { }
            }
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(nameInput.Text) || string.IsNullOrWhiteSpace(appIdInput.Text))
            {
                MessageBox.Show("Please enter both a Game Name and an App ID.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var newGame = new TrackedGame { Name = nameInput.Text, AppId = appIdInput.Text };
            games.Add(newGame);
            SaveGames();
            RefreshGameListUI();

            nameInput.Text = "";
            appIdInput.Text = "";

            Program.UpdateWatchers(games);
            Program.TriggerDataDownload(games, SavedApiKey); 
            MessageBox.Show($"Started tracking {newGame.Name}!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void LoadGames()
        {
            if (File.Exists(gamesFilePath))
            {
                try {
                    string json = File.ReadAllText(gamesFilePath);
                    if (!string.IsNullOrWhiteSpace(json)) 
                        games = JsonSerializer.Deserialize<List<TrackedGame>>(json) ?? new List<TrackedGame>();
                } catch { games = new List<TrackedGame>(); }
            }
        }

        private void SaveGames()
        {
            File.WriteAllText(gamesFilePath, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void RefreshGameListUI()
        {
            gameList.Items.Clear();
            foreach (var game in games) gameList.Items.Add($"{game.Name} ({game.AppId})");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
            base.OnFormClosing(e);
        }
        
        public List<TrackedGame> GetTrackedGames() => games;
    }
}