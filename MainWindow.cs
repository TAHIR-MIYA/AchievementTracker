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
        private List<TrackedGame> games = new List<TrackedGame>();
        private string gamesFilePath = "tracked_games.json";

        public MainWindow()
        {
            this.Text = "Universal Achievement Tracker";
            this.Size = new Size(500, 350);
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;

            // Load saved games from hard drive
            LoadGames();

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
                Size = new Size(200, 240),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10),
                BorderStyle = BorderStyle.FixedSingle
            };
            RefreshGameListUI();
            this.Controls.Add(gameList);

            // --- Right Side: Add New Game ---
            Label addLabel = new Label { Text = "Add New Game (Goldberg)", Location = new Point(240, 20), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
            this.Controls.Add(addLabel);

            Label nameLabel = new Label { Text = "Game Name:", Location = new Point(240, 60), AutoSize = true };
            this.Controls.Add(nameLabel);
            nameInput = new TextBox { Location = new Point(240, 80), Size = new Size(200, 25), BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
            this.Controls.Add(nameInput);

            Label appIdLabel = new Label { Text = "App ID (e.g. 123456):", Location = new Point(240, 115), AutoSize = true };
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

            // Tell the core engine to update its watchers!
            Program.UpdateWatchers(games);
            
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
                    {
                        games = JsonSerializer.Deserialize<List<TrackedGame>>(json) ?? new List<TrackedGame>();
                    }
                }
                catch { /* If the file is corrupted or empty, just ignore it and start fresh */ }
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

        // When the user clicks the red X, we hide the window to the tray instead of quitting
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // Stop the window from actually destroying itself
                this.Hide();     // Just make it invisible
            }
            base.OnFormClosing(e);
        }
        
        // Let the engine fetch the games on startup
        public List<TrackedGame> GetTrackedGames() => games;
    }
}