using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class AchievementRow
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Unlocked { get; set; }
        public bool Hidden { get; set; }
        public Image? Icon { get; set; }
    }

    public class GameDetailsWindow : Form
    {
        private TrackedGame game;
        private string apiKey;
        private HashSet<string> unlockedAchievements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient httpClient = new HttpClient();

        private RowListBox achievementList = null!;
        private ProgressRing ring = null!;
        private Label subtitleLabel = null!;

        public GameDetailsWindow(TrackedGame game, string apiKey)
        {
            this.game = game;
            this.apiKey = apiKey;

            Text = game.Name + " - Achievements";
            ClientSize = new Size(900, 640);
            MinimumSize = new Size(700, 520);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            try { if (File.Exists("app_icon.ico")) Icon = new Icon("app_icon.ico"); } catch { }
            Theme.StyleWindow(this);

            InitializeUI();
            LoadAchievementsAsync();
        }

        private void InitializeUI()
        {
            // List area (Fill) is added first, header (Top) second.
            Panel listArea = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Base, Padding = new Padding(24, 16, 24, 24) };
            achievementList = new RowListBox { Dock = DockStyle.Fill, ItemHeight = 84, BackColor = Theme.Base };
            achievementList.DrawItem += AchievementList_DrawItem;
            listArea.Controls.Add(achievementList);

            Panel headerPanel = new Panel { Dock = DockStyle.Top, Height = 132, BackColor = Theme.Surface };
            ring = new ProgressRing { Size = new Size(84, 84), Location = new Point(28, 24), Backdrop = Theme.Surface };

            Label titleLabel = new Label
            {
                Text = game.Name,
                Font = Theme.Title,
                ForeColor = Theme.Text,
                BackColor = Theme.Surface,
                AutoEllipsis = true,
                Location = new Point(136, 32),
                Size = new Size(700, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            subtitleLabel = new Label
            {
                Text = "Loading achievements...",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                AutoEllipsis = true,
                Location = new Point(138, 74),
                Size = new Size(700, 24),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            headerPanel.Controls.Add(ring);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Resize += (s, e) =>
            {
                titleLabel.Width = Math.Max(100, headerPanel.Width - 136 - 28);
                subtitleLabel.Width = Math.Max(100, headerPanel.Width - 138 - 28);
            };

            Controls.Add(listArea);
            Controls.Add(headerPanel);
        }

        private void AchievementList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= achievementList.Items.Count) return;
            AchievementRow? row = achievementList.Items[e.Index] as AchievementRow;
            if (row == null) return;

            Graphics g = e.Graphics;
            bool hover = achievementList.HoverIndex == e.Index;
            Rectangle r = new Rectangle(e.Bounds.X, e.Bounds.Y + 4, e.Bounds.Width - 2, e.Bounds.Height - 8);

            using (GraphicsPath path = Theme.RoundedRect(r, 12f))
            using (SolidBrush fill = new SolidBrush(hover ? Theme.Raised : Theme.Surface))
            using (Pen pen = new Pen(row.Unlocked ? Theme.Blend(Theme.Border, Theme.Accent, 0.55f) : Theme.Border, 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }

            // Icon
            Rectangle iconRect = new Rectangle(r.X + 14, r.Y + (r.Height - 52) / 2, 52, 52);
            using (GraphicsPath iconPath = Theme.RoundedRect(iconRect, 10f))
            {
                bool drewIcon = false;
                Image? icon = row.Icon;
                if (icon != null)
                {
                    GraphicsState state = g.Save();
                    try
                    {
                        g.SetClip(iconPath);
                        g.DrawImage(icon, iconRect);
                        drewIcon = true;
                    }
                    catch
                    {
                        row.Icon = null;   // the image was disposed or is invalid: fall back to the placeholder
                    }
                    finally
                    {
                        g.Restore(state);
                    }
                }
                if (!drewIcon)
                {
                    using (SolidBrush placeholder = new SolidBrush(Theme.Raised)) g.FillPath(placeholder, iconPath);
                }
            }

            int textX = iconRect.Right + 16;
            int statusWidth = 110;
            int textWidth = Math.Max(40, r.Right - textX - statusWidth - 16);

            Color nameColor = row.Unlocked ? Theme.Text : Theme.TextDim;
            Theme.DrawText(g, row.Name, Theme.BodyStrong, nameColor, new Rectangle(textX, r.Y + 12, textWidth, 22));

            string description = row.Hidden && !row.Unlocked ? "Hidden achievement. Keep playing to reveal it." : row.Description;
            TextRenderer.DrawText(g, description, Theme.Small, new Rectangle(textX, r.Y + 36, textWidth, 32), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            Rectangle statusRect = new Rectangle(r.Right - statusWidth - 16, r.Y, statusWidth, r.Height);
            Theme.DrawText(g, row.Unlocked ? "Unlocked" : "Locked", Theme.BodyStrong, row.Unlocked ? Theme.Accent : Theme.Muted, statusRect, TextFormatFlags.Right);
        }

        private async void LoadAchievementsAsync()
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                subtitleLabel.Text = "Add your Steam API key in Settings to see achievements.";
                return;
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
                            try
                            {
                                foreach (var entry in AchievementIni.Read(file))
                                {
                                    if (entry.Value) unlockedAchievements.Add(entry.Key);
                                }
                            }
                            catch { }
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

                if (!response.IsSuccessStatusCode)
                {
                    subtitleLabel.Text = "Steam rejected the request. Check your API key and the App ID.";
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);

                if (!(doc.RootElement.TryGetProperty("game", out JsonElement gameElement) &&
                      gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) &&
                      statsElement.TryGetProperty("achievements", out JsonElement achievementsElement)))
                {
                    subtitleLabel.Text = "No achievements found for this App ID.";
                    return;
                }

                int unlockedCount = 0;
                int totalCount = 0;
                List<(AchievementRow Row, string IconUrl)> unlocked = new List<(AchievementRow, string)>();
                List<(AchievementRow Row, string IconUrl)> locked = new List<(AchievementRow, string)>();
                List<(AchievementRow Row, string IconUrl)> hidden = new List<(AchievementRow, string)>();

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

                    AchievementRow row = new AchievementRow { Name = displayName, Description = description, Unlocked = isUnlocked, Hidden = isHidden };
                    string targetIcon = isUnlocked ? iconUrl : iconGrayUrl;

                    if (isUnlocked) unlocked.Add((row, targetIcon));
                    else if (isHidden) hidden.Add((row, targetIcon));
                    else locked.Add((row, targetIcon));
                }

                // Unlocked first, then locked, then hidden
                List<(AchievementRow Row, string IconUrl)> ordered = unlocked.Concat(locked).Concat(hidden).ToList();

                achievementList.BeginUpdate();
                achievementList.Items.Clear();
                foreach (var item in ordered) achievementList.Items.Add(item.Row);
                achievementList.EndUpdate();

                ring.Value = totalCount == 0 ? 0f : (float)unlockedCount / totalCount;
                subtitleLabel.Text = unlockedCount + " of " + totalCount + " achievements unlocked";

                foreach (var item in ordered)
                {
                    if (!string.IsNullOrEmpty(item.IconUrl)) _ = LoadIconAsync(item.IconUrl, item.Row);
                }
            }
            catch
            {
                if (!IsDisposed) subtitleLabel.Text = "Couldn't reach Steam. Check your connection and API key.";
            }
        }

        private async Task LoadIconAsync(string url, AchievementRow row)
        {
            try
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(url).ConfigureAwait(false);

                Bitmap copy;
                using (MemoryStream ms = new MemoryStream(imageBytes))
                using (Image img = Image.FromStream(ms))
                {
                    copy = new Bitmap(img);   // independent copy, doesn't depend on the stream
                }

                if (IsDisposed || !IsHandleCreated)
                {
                    copy.Dispose();
                    return;
                }

                // Always hand the finished bitmap to the UI thread, which is the only thread that paints it.
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) { copy.Dispose(); return; }
                    Image? previous = row.Icon;
                    row.Icon = copy;
                    achievementList.Invalidate();
                    previous?.Dispose();
                }));
            }
            catch { }
        }

        // Icons are released only after the list control itself is gone, so nothing can paint a disposed image.
        protected override void Dispose(bool disposing)
        {
            List<Image> icons = new List<Image>();
            if (disposing && achievementList != null)
            {
                foreach (object item in achievementList.Items)
                {
                    if (item is AchievementRow row && row.Icon != null)
                    {
                        icons.Add(row.Icon);
                        row.Icon = null;
                    }
                }
            }
            base.Dispose(disposing);
            foreach (Image img in icons) img.Dispose();
        }
    }
}
