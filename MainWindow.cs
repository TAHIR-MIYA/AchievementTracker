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
            this.Size = new Size(500, 440); // Slightly taller for the new button
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
            // --- Left Side: Game List ---
            Label listLabel = new Label { Text = "Tracked Games:", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            this.Controls.Add(listLabel);

            gameList = new ListBox
            {
                Location = new Point(20, 45),
                Size = new Size(200, 250),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            RefreshGameListUI();
            this.Controls.Add(gameList);

            // --- Right Side: Add New Game ---
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

            // --- THE NEW AUTO-DETECT BUTTON ---
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

            // --- Bottom Section: Global Settings ---
            Panel divider = new Panel { Size = new Size(440, 1), BackColor = Color.Gray, Location = new Point(20, 310) };
            this.Controls.Add(divider);

            Label apiLabel = new Label { Text = "Steam API Key (For real achievement names):", Location = new Point(20, 325), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            this.Controls.Add(apiLabel);
            
            apiInput = new TextBox { 
                Location = new Point(20, 345), 
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
                Location = new Point(330, 344),
                Size = new Size(110, 27),
                BackColor = Color.MediumSeaGreen,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            saveApiButton.FlatAppearance.BorderSize = 0;
            saveApiButton.Click += SaveApiButton_Click;
            this.Controls.Add(saveApiButton);
        }

        // --- NEW AUTO DETECT LOGIC ---
        private void AutoDetectBtn_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Game Executable (*.exe)|*.exe";
                openFileDialog.Title = "Select the cracked game's main .exe file";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string exePath = openFileDialog.FileName;
                    string dir = Path.GetDirectoryName(exePath) ?? "";
                    string detectedAppId = "";

                    // Strategy 1: Look for standard steam_appid.txt
                    string appIdPath = Path.Combine(dir, "steam_appid.txt");
                    if (File.Exists(appIdPath)) detectedAppId = File.ReadAllText(appIdPath).Trim();

                    // Strategy 2: Look for Goldberg's hidden steam_appid.txt
                    if (string.IsNullOrEmpty(detectedAppId))
                    {
                        appIdPath = Path.Combine(dir, "steam_settings", "steam_appid.txt");
                        if (File.Exists(appIdPath)) detectedAppId = File.ReadAllText(appIdPath).Trim();
                    }

                    // Strategy 3: Read inside CODEX steam_emu.ini
                    if (string.IsNullOrEmpty(detectedAppId))
                    {
                        string iniPath = Path.Combine(dir, "steam_emu.ini");
                        if (File.Exists(iniPath))
                        {
                            foreach (string line in File.ReadLines(iniPath))
                            {
                                if (line.Trim().StartsWith("AppId="))
                                {
                                    detectedAppId = line.Split('=')[1].Trim();
                                    break;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(detectedAppId))
                    {
                        appIdInput.Text = detectedAppId;
                        nameInput.Text = new DirectoryInfo(dir).Name; // Uses the folder name as a guess!
                    }
                    else
                    {
                        MessageBox.Show("Could not automatically find the Steam App ID. You may need to enter it manually.", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                    {
                        SavedApiKey = keyElement.GetString() ?? "";
                    }
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
                try 
                {
                    string json = File.ReadAllText(gamesFilePath);
                    if (!string.IsNullOrWhiteSpace(json)) 
                        games = JsonSerializer.Deserialize<List<TrackedGame>>(json) ?? new List<TrackedGame>();
                }
                catch { games = new List<TrackedGame>(); }
            }
        }

        private void SaveGames()
        {
            string json = JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(gamesFilePath, json);
        }

        private void RefreshGameListUI()
        {
            gameList.Items.Clear();
            foreach (var game in games)
            {
                gameList.Items.Add($"{game.Name} ({game.AppId})");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();     
            }
            base.OnFormClosing(e);
        }
        
        public List<TrackedGame> GetTrackedGames() => games;
    }
}