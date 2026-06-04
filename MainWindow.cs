using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using System.Linq;

namespace AchievementTracker
{
    public class TrackedGame
    {
        public string Name { get; set; } = "";
        public string AppId { get; set; } = "";
    }

    public class MainWindow : Form
    {
        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);

        private ListBox gameList = new ListBox();
        private TextBox nameInput = new TextBox();
        private TextBox appIdInput = new TextBox();
        private TextBox apiInput = new TextBox(); 
        
        private List<TrackedGame> games = new List<TrackedGame>();
        private string gamesFilePath = "tracked_games.json";
        private string settingsFilePath = "app_settings.json"; 

        public string SavedApiKey { get; private set; } = "";

        // Steam Color Palette
        private Color steamBg = ColorTranslator.FromHtml("#1b2838");
        private Color steamPanel = ColorTranslator.FromHtml("#171a21");
        private Color steamText = ColorTranslator.FromHtml("#c7d5e0");
        private Color steamBlue = ColorTranslator.FromHtml("#66c0f4");
        private Color steamButtonBg = ColorTranslator.FromHtml("#2a475e");
        private Color steamGreen = ColorTranslator.FromHtml("#5c7e10");

        public MainWindow()
        {
            this.Text = "Universal Achievement Tracker";
            this.Size = new Size(600, 480); 
            this.BackColor = steamBg;
            this.ForeColor = steamText;
            
            // ALLOW RESIZING AND MAXIMIZING
            this.FormBorderStyle = FormBorderStyle.Sizable; 
            this.MaximizeBox = true; 
            
            this.StartPosition = FormStartPosition.CenterScreen;
            if (File.Exists("app_icon.ico")) this.Icon = new Icon("app_icon.ico");

            // Force dark title bar
            if (Environment.OSVersion.Version.Major >= 10)
                DwmSetWindowAttribute(this.Handle, 20, new[] { 1 }, 4);

            LoadGames();
            LoadSettings(); 
            InitializeUI();
        }

        private void InitializeUI()
        {
            // Header
            Panel headerPanel = new Panel { Size = new Size(600, 60), BackColor = steamPanel, Location = new Point(0, 0), Dock = DockStyle.Top };
            Label titleLabel = new Label { Text = "LIBRARY", Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = steamBlue, Location = new Point(20, 15), AutoSize = true };
            headerPanel.Controls.Add(titleLabel);
            this.Controls.Add(headerPanel);

            // Left Side - Game List
            Label listLabel = new Label { Text = "GAMES", Location = new Point(20, 75), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White };
            this.Controls.Add(listLabel);

            gameList = new ListBox
            {
                Location = new Point(20, 100),
                Size = new Size(240, 240),
                BackColor = steamPanel,
                ForeColor = steamText,
                Font = new Font("Segoe UI", 11),
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 35,
                // Stretch list up, down, left, and right
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right 
            };
            gameList.DrawItem += GameList_DrawItem;
            gameList.DoubleClick += GameList_DoubleClick;
            RefreshGameListUI();
            this.Controls.Add(gameList);

            Button removeButton = CreateStyledButton("Remove Selected Game", new Point(20, 350), new Size(240, 30), ColorTranslator.FromHtml("#3d4450"), Color.White);
            removeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; // Stick to bottom left
            removeButton.Click += RemoveButton_Click;
            this.Controls.Add(removeButton);

            // Right Side - Add Game Form
            Label addLabel = new Label { Text = "ADD A GAME", Location = new Point(290, 75), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            this.Controls.Add(addLabel);

            Label nameLabel = new Label { Text = "Game Name:", Location = new Point(290, 105), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            this.Controls.Add(nameLabel);
            nameInput = CreateStyledTextBox(new Point(290, 125));
            nameInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(nameInput);

            Label appIdLabel = new Label { Text = "Steam App ID (e.g. 1091500):", Location = new Point(290, 160), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            this.Controls.Add(appIdLabel);
            appIdInput = CreateStyledTextBox(new Point(290, 180));
            appIdInput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(appIdInput);

            Button addButton = CreateStyledButton("Add to Tracker", new Point(290, 225), new Size(260, 35), steamButtonBg, steamBlue);
            addButton.Anchor = AnchorStyles.Top | AnchorStyles.Right; // Stick to top right
            addButton.Click += AddButton_Click;
            this.Controls.Add(addButton);

            Button autoDetectButton = CreateStyledButton("🔍 Auto-Detect (.exe)", new Point(290, 270), new Size(260, 35), steamPanel, steamText);
            autoDetectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            autoDetectButton.Click += AutoDetectBtn_Click;
            this.Controls.Add(autoDetectButton);

            Panel footerPanel = new Panel { Size = new Size(600, 70), BackColor = steamPanel, Location = new Point(0, 400), Dock = DockStyle.Bottom };
            Label apiLabel = new Label { Text = "Steam Developer API Key:", Location = new Point(20, 10), AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Regular), ForeColor = steamText };
            footerPanel.Controls.Add(apiLabel);
            
            apiInput = new TextBox { 
                Location = new Point(20, 28), 
                Size = new Size(380, 25), 
                BackColor = steamBg, 
                ForeColor = Color.White, 
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9),
                Text = SavedApiKey,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right // Stretch width
            };
            footerPanel.Controls.Add(apiInput);

            Button saveApiButton = CreateStyledButton("Save Key", new Point(410, 26), new Size(140, 28), steamGreen, Color.White);
            saveApiButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            saveApiButton.Click += SaveApiButton_Click;
            footerPanel.Controls.Add(saveApiButton);
            this.Controls.Add(footerPanel);
        }

        private TextBox CreateStyledTextBox(Point location)
        {
            return new TextBox { Location = location, Size = new Size(260, 25), BackColor = steamPanel, ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 10) };
        }

        private Button CreateStyledButton(string text, Point location, Size size, Color bg, Color fg)
        {
            Button btn = new Button
            {
                Text = text, Location = location, Size = size, BackColor = bg, ForeColor = fg,
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => { btn.BackColor = steamBlue; btn.ForeColor = Color.White; };
            btn.MouseLeave += (s, e) => { btn.BackColor = bg; btn.ForeColor = fg; };
            return btn;
        }

        private void GameList_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            
            e.Graphics.FillRectangle(new SolidBrush(isSelected ? steamButtonBg : steamPanel), e.Bounds);
            e.Graphics.DrawString(gameList.Items[e.Index].ToString(), gameList.Font, new SolidBrush(isSelected ? Color.White : steamText), e.Bounds.X + 10, e.Bounds.Y + 8);
        }

        private void GameList_DoubleClick(object? sender, EventArgs e)
        {
            if (gameList.SelectedIndex == -1) return;
            string selectedText = gameList.SelectedItem?.ToString() ?? "";
            var match = Regex.Match(selectedText, @"\(([^)]+)\)");
            if (match.Success)
            {
                string appId = match.Groups[1].Value;
                var game = games.FirstOrDefault(g => g.AppId == appId);
                if (game != null)
                {
                    GameDetailsWindow details = new GameDetailsWindow(game, SavedApiKey);
                    details.ShowDialog();
                }
            }
        }

        private void RemoveButton_Click(object? sender, EventArgs e)
        {
            if (gameList.SelectedIndex == -1) return;
            string selectedText = gameList.SelectedItem?.ToString() ?? "";
            var match = Regex.Match(selectedText, @"\(([^)]+)\)");
            if (match.Success)
            {
                if (MessageBox.Show($"Remove this game from tracking?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    games.RemoveAll(g => g.AppId == match.Groups[1].Value);
                    SaveGames(); RefreshGameListUI();
                    Program.UpdateWatchers(games);
                }
            }
        }

        private void AutoDetectBtn_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Game Executable (*.exe)|*.exe", Title = "Select the game's .exe" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string gameFolder = Path.GetDirectoryName(ofd.FileName) ?? "";
                    string potentialName = Path.GetFileNameWithoutExtension(ofd.FileName);
                    string? detectedAppId = null;

                    string[] filesToScan = { "steam_appid.txt", "steam_emu.ini", "FLT.ini", "tenoke.ini", "OnlineFix.ini" };

                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(gameFolder, "*.*", SearchOption.AllDirectories))
                        {
                            string fileName = Path.GetFileName(file);
                            if (filesToScan.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                            {
                                string content = File.ReadAllText(file);
                                if (fileName.Equals("steam_appid.txt", StringComparison.OrdinalIgnoreCase)) detectedAppId = content.Trim();
                                else
                                {
                                    var match = Regex.Match(content, @"AppId\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                                    if (!match.Success) match = Regex.Match(content, @"SteamAppId\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                                    if (match.Success) detectedAppId = match.Groups[1].Value;
                                }
                                if (!string.IsNullOrEmpty(detectedAppId)) break;
                            }
                        }
                    } catch { }

                    if (!string.IsNullOrEmpty(detectedAppId))
                    {
                        nameInput.Text = potentialName; appIdInput.Text = detectedAppId;
                        MessageBox.Show($"App ID {detectedAppId} found! Click Add.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        MessageBox.Show("Could not find a configuration file with an App ID.", "Detection Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private void SaveApiButton_Click(object? sender, EventArgs e)
        {
            SavedApiKey = apiInput.Text.Trim();
            File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(new { SteamApiKey = SavedApiKey }));
            MessageBox.Show("API Key Saved!", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Program.TriggerDataDownload(games, SavedApiKey);
        }

        private void LoadSettings()
        {
            if (File.Exists(settingsFilePath))
            {
                try {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(settingsFilePath));
                    if (doc.RootElement.TryGetProperty("SteamApiKey", out JsonElement key)) SavedApiKey = key.GetString() ?? "";
                } catch { }
            }
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(nameInput.Text) || string.IsNullOrWhiteSpace(appIdInput.Text)) return;
            games.Add(new TrackedGame { Name = nameInput.Text, AppId = appIdInput.Text });
            SaveGames(); RefreshGameListUI();
            nameInput.Text = ""; appIdInput.Text = "";
            Program.UpdateWatchers(games); Program.TriggerDataDownload(games, SavedApiKey); 
        }

        private void LoadGames()
        {
            if (File.Exists(gamesFilePath))
            {
                try {
                    string json = File.ReadAllText(gamesFilePath);
                    if (!string.IsNullOrWhiteSpace(json)) games = JsonSerializer.Deserialize<List<TrackedGame>>(json) ?? new List<TrackedGame>();
                } catch { games = new List<TrackedGame>(); }
            }
        }

        private void SaveGames() => File.WriteAllText(gamesFilePath, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true }));
        
        private void RefreshGameListUI()
        {
            gameList.Items.Clear();
            foreach (var game in games) gameList.Items.Add($"{game.Name} ({game.AppId})");
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); } base.OnFormClosing(e); }
        public List<TrackedGame> GetTrackedGames() => games;
    }
}