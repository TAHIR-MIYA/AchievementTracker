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
        public double? Percent { get; set; }   // share of all Steam players who unlocked it
        public DateTime? UnlockedAt { get; set; }
        public string? FallbackIconUrl { get; set; }
        public Image? Icon { get; set; }
    }

    public class GameDetailsWindow : Form
    {
        private TrackedGame game;
        private string apiKey;
        private HashSet<string> unlockedAchievements = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DateTime> unlockTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private UnlockChart unlockChart = null!;
        private Action layoutHeader = () => { };
        private static readonly HttpClient httpClient = new HttpClient();

        private RowListBox achievementList = null!;
        private ProgressRing ring = null!;
        private List<AchievementRow> allRows = new List<AchievementRow>();   // everything, before filtering
        private ModernTextBox searchBox = null!;
        private SegmentedControl filterPicker = null!;
        private Label noResults = null!;
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
            System.Drawing.Icon? appIcon = AppPaths.LoadIcon();
            if (appIcon != null) Icon = appIcon;
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

            // Search box and All / Unlocked / Locked filter above the list
            Panel filterBar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = Theme.Base };
            searchBox = new ModernTextBox { PlaceholderText = "Search achievements", Backdrop = Theme.Base };
            filterPicker = new SegmentedControl { Items = new[] { "All", "Unlocked", "Locked", "Recent" }, Backdrop = Theme.Base };
            searchBox.Inner.TextChanged += (s, e) => ApplyFilter();
            filterPicker.SelectedChanged += (s, e) => ApplyFilter();
            filterBar.Controls.Add(searchBox);
            filterBar.Controls.Add(filterPicker);
            filterBar.Resize += (s, e) =>
            {
                int pickerWidth = 440;
                filterPicker.SetBounds(filterBar.Width - pickerWidth, 0, pickerWidth, 38);
                searchBox.SetBounds(0, 0, Math.Max(120, Math.Min(360, filterBar.Width - pickerWidth - 16)), 38);
            };
            listArea.Controls.Add(filterBar);

            noResults = new Label
            {
                Text = "No achievements match.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Base,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            listArea.Controls.Add(noResults);
            noResults.BringToFront();
            listArea.Resize += (s, e) => noResults.SetBounds(24, 16 + 54 + 20, Math.Max(10, listArea.Width - 48), 40);

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

            unlockChart = new UnlockChart { Size = new Size(320, 84), Backdrop = Theme.Surface, Visible = false };

            headerPanel.Controls.Add(ring);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);
            headerPanel.Controls.Add(unlockChart);
            layoutHeader = () =>
            {
                int reserved = unlockChart.Visible ? unlockChart.Width + 24 : 0;
                unlockChart.Location = new Point(headerPanel.Width - 28 - unlockChart.Width, 24);
                titleLabel.Width = Math.Max(100, headerPanel.Width - 136 - 28 - reserved);
                subtitleLabel.Width = Math.Max(100, headerPanel.Width - 138 - 28 - reserved);
            };
            headerPanel.Resize += (s, e) => layoutHeader();

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
            int statusWidth = 190;
            int textWidth = Math.Max(40, r.Right - textX - statusWidth - 16);

            Color nameColor = row.Unlocked ? Theme.Text : Theme.TextDim;
            Theme.DrawText(g, row.Name, Theme.BodyStrong, nameColor, new Rectangle(textX, r.Y + 12, textWidth, 22));

            string description = row.Hidden && !row.Unlocked ? "Hidden achievement. Keep playing to reveal it." : row.Description;
            TextRenderer.DrawText(g, description, Theme.Small, new Rectangle(textX, r.Y + 36, textWidth, 32), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            int statusX = r.Right - statusWidth - 16;
            Rectangle statusRect = row.Percent.HasValue
                ? new Rectangle(statusX, r.Y + 14, statusWidth, 24)
                : new Rectangle(statusX, r.Y, statusWidth, r.Height);
            string statusText = row.Unlocked
                ? (row.UnlockedAt.HasValue ? "Unlocked, " + row.UnlockedAt.Value.ToString("d MMM yyyy") : "Unlocked")
                : "Locked";
            Theme.DrawText(g, statusText, Theme.BodyStrong, row.Unlocked ? Theme.Accent : Theme.Muted, statusRect, TextFormatFlags.Right);

            if (row.Percent.HasValue)
            {
                double percent = row.Percent.Value;
                Theme.DrawText(g, Rarity.Label(percent), Theme.Small, Rarity.IsHighlighted(percent) ? Theme.Accent : Theme.Muted,
                    new Rectangle(statusX, r.Y + 42, statusWidth, 20), TextFormatFlags.Right);
            }
        }

        private async void LoadAchievementsAsync()
        {
            ScanLocalEmulatorSaves();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (!await FetchFromLocalData())
                    subtitleLabel.Text = "Add your Steam API key in Settings to see achievements.";
                return;
            }
            await FetchAndPopulateSteamData();
        }

        private void ScanLocalEmulatorSaves()
        {
            string appId = game.AppId;
            string publicDocs = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

            // 1. GOLDBERG (standard location)
            string goldbergPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Goldberg SteamEmu Saves", appId, "achievements.json");
            ScanGoldbergFile(goldbergPath);

            // GSE stores shared achievement files; include only keys known to this game.
            foreach (var folder in Program.GetGseAchievementFolders())
                ScanGoldbergFile(Path.Combine(folder.Dir, "achievements.json"), onlyKnownAchievements: true);

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
                                foreach (var entry in AchievementIni.ReadWithTimes(file))
                                {
                                    if (!entry.Value.Unlocked) continue;
                                    unlockedAchievements.Add(entry.Key);
                                    if (entry.Value.Time.HasValue) RememberUnlockTime(entry.Key, entry.Value.Time.Value);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        // If several files know the time, the earliest wins.
        private void RememberUnlockTime(string achievementName, DateTime time)
        {
            if (!unlockTimes.TryGetValue(achievementName, out DateTime existing) || time < existing) unlockTimes[achievementName] = time;
        }

        private void ScanGoldbergFile(string path, bool onlyKnownAchievements = false)
        {
            if (!File.Exists(path)) return;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (onlyKnownAchievements && !Program.achievementNames.ContainsKey(game.AppId + "_" + prop.Name)) continue;
                    if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("earned", out JsonElement earned) && earned.GetBoolean())
                    {
                        unlockedAchievements.Add(prop.Name);
                        if (prop.Value.TryGetProperty("earned_time", out JsonElement earnedTime) && earnedTime.ValueKind == JsonValueKind.Number &&
                            earnedTime.TryGetInt64(out long seconds) && seconds > 0)
                        {
                            RememberUnlockTime(prop.Name, DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime);
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.True) unlockedAchievements.Add(prop.Name);
                }
            }
            catch { }
        }

        private async Task FetchAndPopulateSteamData()
        {
            try
            {
                string url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={game.AppId}";
                HttpResponseMessage response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    if (await FetchFromLocalData()) return;
                    subtitleLabel.Text = "Steam rejected the request. Check your API key and the App ID.";
                    return;
                }

                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(json);

                if (!(doc.RootElement.TryGetProperty("game", out JsonElement gameElement) &&
                      gameElement.TryGetProperty("availableGameStats", out JsonElement statsElement) &&
                      statsElement.TryGetProperty("achievements", out JsonElement achievementsElement)))
                {
                    if (await FetchFromLocalData()) return;
                    subtitleLabel.Text = "No achievements found for this App ID.";
                    return;
                }

                await Program.EnsureRarityAsync(game.AppId);

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
                    if (Program.TryGetRarity(game.AppId, internalName, out double rarityPercent)) row.Percent = rarityPercent;
                    if (isUnlocked && unlockTimes.TryGetValue(internalName, out DateTime unlockedAt)) row.UnlockedAt = unlockedAt;
                    string targetIcon = isUnlocked ? iconUrl : iconGrayUrl;
                    if (Program.achievementIcons.TryGetValue(game.AppId + "_" + internalName, out string? cachedIconUrl))
                    {
                        row.FallbackIconUrl = cachedIconUrl;
                        if (string.IsNullOrWhiteSpace(targetIcon)) targetIcon = cachedIconUrl;
                    }

                    if (isUnlocked) unlocked.Add((row, targetIcon));
                    else if (isHidden) hidden.Add((row, targetIcon));
                    else locked.Add((row, targetIcon));
                }

                // Unlocked first, then locked, then hidden
                List<(AchievementRow Row, string IconUrl)> ordered = unlocked.Concat(locked).Concat(hidden).ToList();

                allRows = ordered.Select(item => item.Row).ToList();
                filterPicker.Items = new[]
                {
                    "All " + totalCount,
                    "Unlocked " + unlockedCount,
                    "Locked " + (totalCount - unlockedCount),
                    "Recent"
                };
                ApplyFilter();

                ring.Value = totalCount == 0 ? 0f : (float)unlockedCount / totalCount;
                subtitleLabel.Text = unlockedCount + " of " + totalCount + " achievements unlocked";

                // Timeline: a bar chart of when they were unlocked, and how long it took
                List<DateTime> times = allRows.Where(row => row.UnlockedAt.HasValue).Select(row => row.UnlockedAt!.Value).ToList();
                if (times.Count >= 2)
                {
                    int days = (int)(times.Max().Date - times.Min().Date).TotalDays + 1;
                    subtitleLabel.Text += ", over " + days + (days == 1 ? " day" : " days");
                    unlockChart.Times = times;
                    unlockChart.Visible = true;
                    layoutHeader();
                }

                foreach (var item in ordered)
                {
                    if (!string.IsNullOrEmpty(item.IconUrl)) _ = LoadIconAsync(item.IconUrl, item.Row, item.Row.FallbackIconUrl);
                }
            }
            catch
            {
                if (!IsDisposed)
                {
                    if (!await FetchFromLocalData())
                        subtitleLabel.Text = "Couldn't reach Steam, and no local achievement data is available.";
                }
            }
        }

        private async Task<bool> FetchFromLocalData()
        {
            try
            {
                string prefix = game.AppId + "_";
                var localNames = Program.achievementNames.Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
                var localIcons = Program.achievementIcons.Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

                if (localNames.Count == 0) return false;

                await Program.EnsureRarityAsync(game.AppId);

                int unlockedCount = 0;
                int totalCount = 0;
                List<(AchievementRow Row, string IconUrl)> unlocked = new List<(AchievementRow, string)>();
                List<(AchievementRow Row, string IconUrl)> locked = new List<(AchievementRow, string)>();
                List<(AchievementRow Row, string IconUrl)> hidden = new List<(AchievementRow, string)>();

                // We don't have descriptions or hidden status from local data, so we'll use minimal info
                foreach (var kvp in localNames)
                {
                    totalCount++;
                    string internalName = kvp.Key.Substring(prefix.Length);
                    string displayName = kvp.Value;
                    string iconUrl = localIcons.FirstOrDefault(i => i.Key == kvp.Key).Value ?? "";

                    bool isUnlocked = unlockedAchievements.Contains(internalName);
                    if (isUnlocked) unlockedCount++;

                    AchievementRow row = new AchievementRow { Name = displayName, Description = "", Unlocked = isUnlocked, Hidden = false };
                    if (Program.TryGetRarity(game.AppId, internalName, out double rarityPercent)) row.Percent = rarityPercent;
                    if (isUnlocked && unlockTimes.TryGetValue(internalName, out DateTime unlockedAt)) row.UnlockedAt = unlockedAt;

                    if (isUnlocked) unlocked.Add((row, iconUrl));
                    else locked.Add((row, iconUrl));
                }

                // Unlocked first, then locked
                List<(AchievementRow Row, string IconUrl)> ordered = unlocked.Concat(locked).ToList();

                allRows = ordered.Select(item => item.Row).ToList();
                filterPicker.Items = new[]
                {
                    "All " + totalCount,
                    "Unlocked " + unlockedCount,
                    "Locked " + (totalCount - unlockedCount),
                    "Recent"
                };
                ApplyFilter();

                ring.Value = totalCount == 0 ? 0f : (float)unlockedCount / totalCount;
                subtitleLabel.Text = unlockedCount + " of " + totalCount + " achievements unlocked (local data)";

                foreach (var item in ordered)
                {
                    if (!string.IsNullOrEmpty(item.IconUrl)) _ = LoadIconAsync(item.IconUrl, item.Row, item.Row.FallbackIconUrl);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void ApplyFilter()
        {
            string term = searchBox.Value.Trim();
            int mode = filterPicker.SelectedIndex;   // 0 = all, 1 = unlocked, 2 = locked

            achievementList.BeginUpdate();
            achievementList.Items.Clear();
            IEnumerable<AchievementRow> source = mode == 3
                ? allRows.Where(item => item.Unlocked).OrderByDescending(item => item.UnlockedAt ?? DateTime.MinValue)
                : allRows;

            foreach (AchievementRow row in source)
            {
                if (mode == 1 && !row.Unlocked) continue;
                if (mode == 2 && row.Unlocked) continue;

                if (term.Length > 0)
                {
                    bool descriptionHidden = row.Hidden && !row.Unlocked;   // never reveal a hidden description through search
                    bool matches = row.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                   (!descriptionHidden && row.Description.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!matches) continue;
                }
                achievementList.Items.Add(row);
            }
            achievementList.EndUpdate();
            noResults.Visible = allRows.Count > 0 && achievementList.Items.Count == 0;
        }

        private async Task LoadIconAsync(string url, AchievementRow row, string? fallbackUrl = null)
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
            catch (Exception ex)
            {
                string? communityAssetUrl = GetCommunityAssetUrl(url);
                if (!string.IsNullOrWhiteSpace(communityAssetUrl))
                {
                    await LoadIconAsync(communityAssetUrl, row, fallbackUrl);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(fallbackUrl) &&
                    !string.Equals(url, fallbackUrl, StringComparison.OrdinalIgnoreCase))
                {
                    await LoadIconAsync(fallbackUrl, row);
                    return;
                }

                if (!(ex is HttpRequestException requestException &&
                      requestException.StatusCode == System.Net.HttpStatusCode.NotFound))
                    System.Diagnostics.Debug.WriteLine($"Failed to load icon from {url}: {ex.Message}");
                CreatePlaceholderIcon(row);
            }
        }

        private static string? GetCommunityAssetUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return null;

            const string legacyPath = "/steamcommunity/public/images/apps/";
            if (!uri.AbsolutePath.StartsWith(legacyPath, StringComparison.OrdinalIgnoreCase)) return null;

            string assetPath = uri.AbsolutePath.Substring(legacyPath.Length);
            return "https://shared.fastly.steamstatic.com/community_assets/images/apps/" + assetPath;
        }

        private void CreatePlaceholderIcon(AchievementRow row)
        {
            if (IsDisposed || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed) return;
                // Create a visually distinct placeholder based on achievement state
                var bmp = new Bitmap(52, 52);
                using (var g = Graphics.FromImage(bmp))
                using (var path = Theme.RoundedRect(new Rectangle(0, 0, 52, 52), 10f))
                {
                    // Background color based on state
                    Color bgColor;
                    Color accentColor;
                    string symbol;

                    if (row.Unlocked)
                    {
                        bgColor = Theme.Blend(Theme.Accent, Theme.Base, 0.15f);
                        accentColor = Theme.Accent;
                        symbol = "✓";
                    }
                    else if (row.Hidden)
                    {
                        bgColor = Theme.Blend(Theme.Danger, Theme.Base, 0.15f);
                        accentColor = Theme.Danger;
                        symbol = "?";
                    }
                    else
                    {
                        bgColor = Theme.Raised;
                        accentColor = Theme.Muted;
                        symbol = !string.IsNullOrEmpty(row.Name) ? row.Name[0].ToString().ToUpper() : "?";
                    }

                    using (var bgBrush = new SolidBrush(bgColor))
                    using (var borderPen = new Pen(Theme.Border, 1.5f))
                    {
                        g.FillPath(bgBrush, path);
                        g.DrawPath(borderPen, path);
                    }

                    // Draw symbol/letter
                    using (var brush = new SolidBrush(accentColor))
                    using (var font = new Font(Theme.BodyStrong.FontFamily, row.Unlocked ? 18f : (row.Hidden ? 16f : 14f), FontStyle.Bold))
                    {
                        var size = g.MeasureString(symbol, font);
                        float x = (52 - size.Width) / 2;
                        float y = (52 - size.Height) / 2;
                        g.DrawString(symbol, font, brush, x, y);
                    }

                    // Add rarity indicator for unlocked achievements
                    if (row.Unlocked && row.Percent.HasValue && Rarity.IsHighlighted(row.Percent.Value))
                    {
                        using (var rareBrush = new SolidBrush(Theme.Accent))
                        {
                            g.FillEllipse(rareBrush, 36, 36, 14, 14);
                            using (var starFont = new Font("Segoe UI Symbol", 9f))
                            {
                                g.DrawString("★", starFont, new SolidBrush(Theme.Base), 38, 35);
                            }
                        }
                    }
                }

                Image? previous = row.Icon;
                row.Icon = bmp;
                achievementList.Invalidate();
                previous?.Dispose();
            }));
        }

        // Icons are released only after the list control itself is gone, so nothing can paint a disposed image.
        protected override void Dispose(bool disposing)
        {
            List<Image> icons = new List<Image>();
            if (disposing)
            {
                foreach (AchievementRow row in allRows)
                {
                    if (row.Icon != null)
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
