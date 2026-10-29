using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class TrackedGame
    {
        public string Name { get; set; } = "";
        public string AppId { get; set; } = "";

        public override string ToString() => Name;
    }

    public class MainWindow : Form
    {
        private List<TrackedGame> games = new List<TrackedGame>();
        private readonly string gamesFilePath = AppPaths.InBase("tracked_games.json");
        public string SavedApiKey { get; private set; } = "";

        // Header
        private Panel header = null!;
        private PictureBox logo = null!;
        private Label titleLabel = null!;
        private Label subtitleLabel = null!;
        private FlatButton libraryTab = null!;
        private FlatButton settingsTab = null!;
        private StatusPill statusPill = null!;

        // Pages
        private Panel libraryPage = null!;
        private Panel settingsPage = null!;

        // Library page
        private RowListBox gameList = null!;
        private Label emptyLabel = null!;
        private ModernTextBox nameInput = null!;
        private ModernTextBox appIdInput = null!;
        private Label detailHint = null!;
        private AvatarBox detailAvatar = null!;
        private Label detailName = null!;
        private Label detailId = null!;
        private FlatButton viewButton = null!;
        private FlatButton removeButton = null!;

        // Settings page
        private ModernTextBox apiInput = null!;

        public MainWindow()
        {
            Text = "Achievement Tracker";
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(900, 600);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            System.Drawing.Icon? appIcon = AppPaths.LoadIcon();
            if (appIcon != null) Icon = appIcon;
            Theme.StyleWindow(this);

            LoadGames();
            LoadSettings();

            Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(28, 4, 28, 28), BackColor = Theme.Base };
            libraryPage = BuildLibraryPage();
            settingsPage = BuildSettingsPage();
            settingsPage.Visible = false;
            body.Controls.Add(libraryPage);
            body.Controls.Add(settingsPage);

            BuildHeader();

            // Fill first, then Top (WinForms docks later-added controls first)
            Controls.Add(body);
            Controls.Add(header);

            RefreshGameListUI();
            UpdateStatus();
            LayoutHeader();
        }

        // ------------------------------------------------------------------ small helpers

        private static Label MakeLabel(string text, Font font, Color color, Color back)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = back, AutoSize = true };
        }

        private static CardPanel NewCard()
        {
            return new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        }

        private static TableLayoutPanel NewTable(int columns, int rows)
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = columns,
                RowCount = rows,
                BackColor = Theme.Base,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }

        // Briefly swaps a button's text to confirm an action ("Copied", "Saved"...) instead of a modal dialog.
        private static void FlashText(FlatButton button, string temporaryText)
        {
            if (button.Tag is string) return;
            button.Tag = button.Text;
            button.Text = temporaryText;
            System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 1600 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                if (button.IsDisposed) return;
                button.Text = (string)button.Tag!;
                button.Tag = null;
            };
            timer.Start();
        }

        // ------------------------------------------------------------------ header

        private void BuildHeader()
        {
            header = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = Theme.Base };

            logo = new PictureBox { Size = new Size(32, 32), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Base };
            try
            {
                System.Drawing.Icon? logoIcon = AppPaths.LoadIcon();
                if (logoIcon != null) logo.Image = new System.Drawing.Icon(logoIcon, new Size(32, 32)).ToBitmap();
            }
            catch { }
            logo.Visible = logo.Image != null;

            titleLabel = MakeLabel("Achievement Tracker", Theme.Title, Theme.Text, Theme.Base);
            subtitleLabel = MakeLabel("", Theme.Small, Theme.Muted, Theme.Base);

            libraryTab = new FlatButton { Text = "Library", Kind = ButtonKind.Ghost, Backdrop = Theme.Base, Size = new Size(92, 40), Active = true };
            settingsTab = new FlatButton { Text = "Settings", Kind = ButtonKind.Ghost, Backdrop = Theme.Base, Size = new Size(92, 40) };
            libraryTab.Click += (s, e) => ShowPage(false);
            settingsTab.Click += (s, e) => ShowPage(true);

            statusPill = new StatusPill { Backdrop = Theme.Base };

            header.Controls.Add(logo);
            header.Controls.Add(titleLabel);
            header.Controls.Add(subtitleLabel);
            header.Controls.Add(libraryTab);
            header.Controls.Add(settingsTab);
            header.Controls.Add(statusPill);
            header.Resize += (s, e) => LayoutHeader();
        }

        private void LayoutHeader()
        {
            if (header == null) return;
            int x = logo.Visible ? 70 : 28;
            logo.Location = new Point(28, 22);
            titleLabel.Location = new Point(x, 12);
            subtitleLabel.Location = new Point(x + 2, 46);

            statusPill.Location = new Point(header.Width - 28 - statusPill.Width, (header.Height - statusPill.Height) / 2);

            int tabsWidth = libraryTab.Width + settingsTab.Width + 8;
            int tabsX = (header.Width - tabsWidth) / 2;
            libraryTab.Location = new Point(tabsX, 18);
            settingsTab.Location = new Point(tabsX + libraryTab.Width + 8, 18);
        }

        private void ShowPage(bool settings)
        {
            libraryPage.Visible = !settings;
            settingsPage.Visible = settings;
            libraryTab.Active = !settings;
            settingsTab.Active = settings;
        }

        private void UpdateStatus()
        {
            if (!AppSettings.Current.ObsOverlay) statusPill.SetStatus("OBS overlay is off", Theme.Muted);
            else if (OverlayServer.IsRunning) statusPill.SetStatus("OBS overlay is running", Theme.Good);
            else statusPill.SetStatus("OBS overlay failed to start", Theme.Danger);
            LayoutHeader();
        }

        // ------------------------------------------------------------------ library page

        private Panel BuildLibraryPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Base };

            TableLayoutPanel layout = NewTable(3, 1);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ---- Left: your games
            CardPanel listCard = NewCard();
            Label listTitle = MakeLabel("Your games", Theme.Heading, Theme.Text, Theme.Surface);
            listTitle.Location = new Point(20, 16);

            gameList = new RowListBox { ItemHeight = 64 };
            gameList.DrawItem += GameList_DrawItem;
            gameList.SelectedIndexChanged += (s, e) => UpdateDetail();
            gameList.DoubleClick += (s, e) => OpenDetails();

            emptyLabel = new Label
            {
                Text = "No games yet.\nAdd one on the right, or pick the game's .exe and the App ID is found for you.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                TextAlign = ContentAlignment.MiddleCenter
            };

            listCard.Controls.Add(listTitle);
            listCard.Controls.Add(gameList);
            listCard.Controls.Add(emptyLabel);
            emptyLabel.BringToFront();
            listCard.Resize += (s, e) =>
            {
                gameList.SetBounds(10, 52, listCard.Width - 20, Math.Max(10, listCard.Height - 62));
                emptyLabel.SetBounds(30, 52, Math.Max(10, listCard.Width - 60), Math.Max(10, listCard.Height - 62));
            };

            // ---- Right column: add card + selected-game card
            TableLayoutPanel right = NewTable(1, 3);
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 186));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            right.Controls.Add(BuildAddCard(), 0, 0);
            right.Controls.Add(BuildDetailCard(), 0, 2);

            layout.Controls.Add(listCard, 0, 0);
            layout.Controls.Add(right, 2, 0);
            page.Controls.Add(layout);
            return page;
        }

        private CardPanel BuildAddCard()
        {
            CardPanel card = NewCard();

            Label title = MakeLabel("Add a game", Theme.Heading, Theme.Text, Theme.Surface);
            Label nameCaption = MakeLabel("Game name", Theme.Small, Theme.Muted, Theme.Surface);
            Label idCaption = MakeLabel("Steam App ID", Theme.Small, Theme.Muted, Theme.Surface);

            nameInput = new ModernTextBox { PlaceholderText = "e.g. Hollow Knight" };
            appIdInput = new ModernTextBox { PlaceholderText = "367520" };
            nameInput.Submitted += (s, e) => appIdInput.Inner.Focus();
            appIdInput.Submitted += (s, e) => AddGame();

            FlatButton addButton = new FlatButton { Text = "Add game", Kind = ButtonKind.Primary };
            addButton.Click += (s, e) => AddGame();

            FlatButton detectButton = new FlatButton { Text = "Detect from .exe", Kind = ButtonKind.Secondary };
            detectButton.Click += (s, e) => AutoDetect(detectButton);

            card.Controls.Add(title);
            card.Controls.Add(nameCaption);
            card.Controls.Add(idCaption);
            card.Controls.Add(nameInput);
            card.Controls.Add(appIdInput);
            card.Controls.Add(addButton);
            card.Controls.Add(detectButton);

            card.Resize += (s, e) =>
            {
                int w = card.Width - 40;
                int idW = 112;
                title.Location = new Point(20, 16);
                nameCaption.Location = new Point(20, 48);
                idCaption.Location = new Point(20 + w - idW, 48);
                nameInput.SetBounds(20, 70, w - idW - 12, 38);
                appIdInput.SetBounds(20 + w - idW, 70, idW, 38);
                addButton.SetBounds(20, 126, 112, 38);
                detectButton.SetBounds(140, 126, 152, 38);
            };
            return card;
        }

        private CardPanel BuildDetailCard()
        {
            CardPanel card = NewCard();

            detailHint = new Label
            {
                Text = "Select a game to see its achievements or remove it. Double-click a game to open it straight away.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface
            };
            detailAvatar = new AvatarBox { Size = new Size(56, 56), Backdrop = Theme.Surface };
            detailName = new Label { Font = Theme.Heading, ForeColor = Theme.Text, BackColor = Theme.Surface, AutoEllipsis = true };
            detailId = new Label { Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Surface, AutoEllipsis = true };

            viewButton = new FlatButton { Text = "View achievements", Kind = ButtonKind.Primary };
            viewButton.Click += (s, e) => OpenDetails();

            removeButton = new FlatButton { Text = "Remove from tracker", Kind = ButtonKind.DangerGhost };
            removeButton.Click += (s, e) => RemoveSelected();

            card.Controls.Add(detailHint);
            card.Controls.Add(detailAvatar);
            card.Controls.Add(detailName);
            card.Controls.Add(detailId);
            card.Controls.Add(viewButton);
            card.Controls.Add(removeButton);

            card.Resize += (s, e) =>
            {
                int w = card.Width - 40;
                detailHint.SetBounds(20, 20, w, 80);
                detailAvatar.Location = new Point(20, 20);
                detailName.SetBounds(90, 24, Math.Max(10, w - 70), 24);
                detailId.SetBounds(90, 50, Math.Max(10, w - 70), 20);
                viewButton.SetBounds(20, 98, w, 38);
                removeButton.SetBounds(20, 144, w, 38);
            };
            return card;
        }

        private TrackedGame? SelectedGame => gameList.SelectedItem as TrackedGame;

        private void UpdateDetail()
        {
            TrackedGame? game = SelectedGame;
            bool has = game != null;
            detailHint.Visible = !has;
            detailAvatar.Visible = has;
            detailName.Visible = has;
            detailId.Visible = has;
            viewButton.Visible = has;
            removeButton.Visible = has;

            if (game != null)
            {
                detailName.Text = game.Name;
                detailId.Text = "App ID " + game.AppId;
                detailAvatar.GameName = game.Name;
                detailAvatar.Seed = game.AppId;
                detailAvatar.Invalidate();
            }
        }

        private void GameList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= gameList.Items.Count) return;
            TrackedGame? game = gameList.Items[e.Index] as TrackedGame;
            if (game == null) return;

            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool hover = gameList.HoverIndex == e.Index;
            Rectangle r = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 3, e.Bounds.Width - 4, e.Bounds.Height - 6);

            using (System.Drawing.Drawing2D.GraphicsPath path = Theme.RoundedRect(r, 12f))
            {
                if (selected || hover)
                {
                    using (SolidBrush fill = new SolidBrush(selected ? Theme.Hover : Theme.Raised)) g.FillPath(fill, path);
                }
                if (selected)
                {
                    using (Pen pen = new Pen(Theme.Accent, 1.5f)) g.DrawPath(pen, path);
                }
            }

            Theme.DrawAvatar(g, new Rectangle(r.X + 12, r.Y + (r.Height - 40) / 2, 40, 40), game.Name, game.AppId);
            Theme.DrawText(g, game.Name, Theme.BodyStrong, Theme.Text, new Rectangle(r.X + 64, r.Y + 10, r.Width - 76, 22));
            Theme.DrawText(g, "App ID " + game.AppId, Theme.Small, Theme.Muted, new Rectangle(r.X + 64, r.Y + 32, r.Width - 76, 18));
        }

        private void RefreshGameListUI(string? selectAppId = null)
        {
            gameList.BeginUpdate();
            gameList.Items.Clear();
            foreach (TrackedGame game in games) gameList.Items.Add(game);
            gameList.EndUpdate();

            emptyLabel.Visible = games.Count == 0;
            subtitleLabel.Text = games.Count == 0 ? "No games tracked yet"
                : games.Count == 1 ? "Watching 1 game"
                : "Watching " + games.Count + " games";

            if (selectAppId != null)
            {
                int idx = games.FindIndex(g => g.AppId == selectAppId);
                if (idx >= 0) gameList.SelectedIndex = idx;
            }
            UpdateDetail();
        }

        private void OpenDetails()
        {
            TrackedGame? game = SelectedGame;
            if (game == null) return;
            using (GameDetailsWindow details = new GameDetailsWindow(game, SavedApiKey))
            {
                details.ShowDialog(this);
            }
        }

        private void RemoveSelected()
        {
            TrackedGame? game = SelectedGame;
            if (game == null) return;
            if (MessageBox.Show("Stop tracking \"" + game.Name + "\"?", "Remove game", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            games.RemoveAll(g => g.AppId == game.AppId);
            SaveGames();
            RefreshGameListUI();
            Program.UpdateWatchers(games);
        }

        private void AddGame()
        {
            string name = nameInput.Value.Trim();
            string id = appIdInput.Value.Trim();

            if (name.Length == 0) { nameInput.Inner.Focus(); return; }
            if (id.Length == 0) { appIdInput.Inner.Focus(); return; }
            if (!id.All(char.IsDigit))
            {
                MessageBox.Show("The Steam App ID should be a number, for example 367520.", "Check the App ID", MessageBoxButtons.OK, MessageBoxIcon.Information);
                appIdInput.Inner.Focus();
                return;
            }
            if (games.Any(g => g.AppId == id))
            {
                MessageBox.Show("That game is already being tracked.", "Already added", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            games.Add(new TrackedGame { Name = name, AppId = id });
            SaveGames();
            RefreshGameListUI(id);
            nameInput.Value = "";
            appIdInput.Value = "";
            Program.UpdateWatchers(games);
            Program.TriggerDataDownload(games, SavedApiKey);
        }

        private void AutoDetect(FlatButton button)
        {
            using (OpenFileDialog ofd = new OpenFileDialog() { Filter = "Game Executable (*.exe)|*.exe", Title = "Select the game's .exe", RestoreDirectory = true })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;

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
                }
                catch { }

                if (!string.IsNullOrEmpty(detectedAppId))
                {
                    nameInput.Value = potentialName;
                    appIdInput.Value = detectedAppId;
                    FlashText(button, "App ID found");
                    nameInput.Inner.Focus();
                }
                else
                {
                    MessageBox.Show("Could not find a configuration file with an App ID next to that game.", "Detection failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ------------------------------------------------------------------ settings page

        private Panel BuildSettingsPage()
        {
            Panel page = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Base };

            TableLayoutPanel layout = NewTable(3, 1);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel right = NewTable(1, 3);
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(BuildApiCard(), 0, 0);
            right.Controls.Add(BuildObsStepsCard(), 0, 2);

            layout.Controls.Add(BuildPopupsCard(), 0, 0);
            layout.Controls.Add(right, 2, 0);
            page.Controls.Add(layout);
            return page;
        }

        private CardPanel BuildPopupsCard()
        {
            CardPanel card = NewCard();
            AppSettings settings = AppSettings.Current;

            Label title = MakeLabel("Popups", Theme.Heading, Theme.Text, Theme.Surface);

            Label desktopLabel = MakeLabel("Desktop popup", Theme.Body, Theme.Text, Theme.Surface);
            Label desktopHint = MakeLabel("Appears on your screen while you play", Theme.Small, Theme.Muted, Theme.Surface);
            ToggleSwitch desktopToggle = new ToggleSwitch { Checked = settings.DesktopPopup };
            desktopToggle.CheckedChanged += (s, e) =>
            {
                AppSettings.Current.DesktopPopup = desktopToggle.Checked;
                AppSettings.Save();
            };

            Label obsLabel = MakeLabel("OBS browser overlay", Theme.Body, Theme.Text, Theme.Surface);
            Label obsHint = MakeLabel("Lets OBS record the popup, even with Game Capture", Theme.Small, Theme.Muted, Theme.Surface);
            ToggleSwitch obsToggle = new ToggleSwitch { Checked = settings.ObsOverlay };
            obsToggle.CheckedChanged += (s, e) =>
            {
                AppSettings.Current.ObsOverlay = obsToggle.Checked;
                AppSettings.Save();
                Program.ApplyOverlaySettings();
                if (obsToggle.Checked && !OverlayServer.IsRunning)
                {
                    MessageBox.Show("Could not start the overlay server on port " + AppSettings.Current.ObsPort + ".\n\n" + OverlayServer.LastError,
                        "OBS overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    obsToggle.Checked = false;   // re-enters this handler and updates the status
                    return;
                }
                UpdateStatus();
            };

            Label volLabel = MakeLabel("Sound volume", Theme.Body, Theme.Text, Theme.Surface);
            Label volValue = new Label
            {
                Text = settings.SoundVolume + "%",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                TextAlign = ContentAlignment.MiddleRight
            };
            ModernSlider volumeSlider = new ModernSlider { Value = settings.SoundVolume };
            volumeSlider.ValueChanged += (s, e) =>
            {
                AppSettings.Current.SoundVolume = volumeSlider.Value;
                volValue.Text = volumeSlider.Value + "%";
            };
            volumeSlider.ValueCommitted += (s, e) => AppSettings.Save();

            FlatButton testButton = new FlatButton { Text = "Test popup", Kind = ButtonKind.Primary };
            testButton.Click += (s, e) => Program.ShowTestPopup();

            FlatButton copyButton = new FlatButton { Text = "Copy OBS URL", Kind = ButtonKind.Secondary };
            copyButton.Click += (s, e) =>
            {
                try { Clipboard.SetText(OverlayServer.Url(AppSettings.Current.ObsPort)); FlashText(copyButton, "Copied"); }
                catch { FlashText(copyButton, "Couldn't copy"); }
            };

            card.Controls.Add(title);
            card.Controls.Add(desktopLabel);
            card.Controls.Add(desktopHint);
            card.Controls.Add(desktopToggle);
            card.Controls.Add(obsLabel);
            card.Controls.Add(obsHint);
            card.Controls.Add(obsToggle);
            card.Controls.Add(volLabel);
            card.Controls.Add(volValue);
            card.Controls.Add(volumeSlider);
            card.Controls.Add(testButton);
            card.Controls.Add(copyButton);

            card.Resize += (s, e) =>
            {
                int w = card.Width - 40;
                title.Location = new Point(20, 16);

                desktopLabel.Location = new Point(20, 54);
                desktopHint.Location = new Point(20, 76);
                desktopToggle.Location = new Point(card.Width - 20 - desktopToggle.Width, 58);

                obsLabel.Location = new Point(20, 108);
                obsHint.Location = new Point(20, 130);
                obsToggle.Location = new Point(card.Width - 20 - obsToggle.Width, 112);

                volLabel.Location = new Point(20, 166);
                volValue.SetBounds(card.Width - 20 - 60, 166, 60, 22);
                volumeSlider.SetBounds(20, 192, w, 28);

                testButton.SetBounds(20, 244, 120, 38);
                copyButton.SetBounds(148, 244, 140, 38);
            };
            return card;
        }

        private CardPanel BuildApiCard()
        {
            CardPanel card = NewCard();

            Label title = MakeLabel("Steam API key", Theme.Heading, Theme.Text, Theme.Surface);
            Label caption = MakeLabel("Used to fetch achievement names, icons and progress.", Theme.Small, Theme.Muted, Theme.Surface);

            apiInput = new ModernTextBox { PlaceholderText = "Paste your Steam Web API key", UseSystemPasswordChar = true };
            apiInput.Value = SavedApiKey;
            apiInput.Submitted += (s, e) => SaveApiKey(null);

            FlatButton showButton = new FlatButton { Text = "Show", Kind = ButtonKind.Ghost };
            showButton.Click += (s, e) =>
            {
                apiInput.UseSystemPasswordChar = !apiInput.UseSystemPasswordChar;
                showButton.Text = apiInput.UseSystemPasswordChar ? "Show" : "Hide";
            };

            FlatButton saveButton = new FlatButton { Text = "Save", Kind = ButtonKind.Primary };
            saveButton.Click += (s, e) => SaveApiKey(saveButton);

            card.Controls.Add(title);
            card.Controls.Add(caption);
            card.Controls.Add(apiInput);
            card.Controls.Add(showButton);
            card.Controls.Add(saveButton);

            card.Resize += (s, e) =>
            {
                int w = card.Width - 40;
                title.Location = new Point(20, 16);
                caption.Location = new Point(20, 44);
                apiInput.SetBounds(20, 76, Math.Max(40, w - 64 - 84 - 16), 38);
                showButton.SetBounds(20 + w - 84 - 8 - 64, 76, 64, 38);
                saveButton.SetBounds(20 + w - 84, 76, 84, 38);
            };
            return card;
        }

        private CardPanel BuildObsStepsCard()
        {
            CardPanel card = NewCard();

            Label title = MakeLabel("Record the popup in OBS", Theme.Heading, Theme.Text, Theme.Surface);
            Label steps = new Label
            {
                Text = "1. Press Copy OBS URL in the Popups card.\n" +
                       "2. In OBS, add a Browser source and paste the URL.\n" +
                       "3. Set its width and height to your canvas, for example 1920 x 1080.\n" +
                       "4. Drag the source above your Game Capture.\n\n" +
                       "No desktop audio in your recording? Add ?sound=1 to the end of the URL.\n\n" +
                       "Popups only appear over the game in Borderless or Windowed mode.",
                Font = Theme.Body,
                ForeColor = Theme.TextDim,
                BackColor = Theme.Surface,
                TextAlign = ContentAlignment.TopLeft
            };

            card.Controls.Add(title);
            card.Controls.Add(steps);
            card.Resize += (s, e) =>
            {
                title.Location = new Point(20, 16);
                steps.SetBounds(20, 50, Math.Max(10, card.Width - 40), Math.Max(10, card.Height - 66));
            };
            return card;
        }

        private void SaveApiKey(FlatButton? button)
        {
            SavedApiKey = apiInput.Value.Trim();
            AppSettings.Current.SteamApiKey = SavedApiKey;
            AppSettings.Save();
            Program.TriggerDataDownload(games, SavedApiKey);
            if (button != null) FlashText(button, "Saved");
        }

        // ------------------------------------------------------------------ data

        private void LoadSettings()
        {
            SavedApiKey = AppSettings.Current.SteamApiKey;
        }

        private void LoadGames()
        {
            if (File.Exists(gamesFilePath))
            {
                try
                {
                    string json = File.ReadAllText(gamesFilePath);
                    if (!string.IsNullOrWhiteSpace(json)) games = JsonSerializer.Deserialize<List<TrackedGame>>(json) ?? new List<TrackedGame>();
                    bool normalized = false;
                    foreach (TrackedGame game in games)
                    {
                        string appId = game.AppId.Trim();
                        if (appId != game.AppId)
                        {
                            game.AppId = appId;
                            normalized = true;
                        }
                    }
                    if (normalized)
                    {
                        try { SaveGames(); } catch { }
                    }
                }
                catch { games = new List<TrackedGame>(); }
            }
        }

        private void SaveGames() => File.WriteAllText(gamesFilePath, JsonSerializer.Serialize(games, new JsonSerializerOptions { WriteIndented = true }));

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; this.Hide(); }
            base.OnFormClosing(e);
        }

        public List<TrackedGame> GetTrackedGames() => games;
    }
}
