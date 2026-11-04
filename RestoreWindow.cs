using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    /// <summary>Guided restore: pick a backup, tick what you want back, and restore it safely.</summary>
    public class RestoreWindow : Form
    {
        private readonly MainWindow owner;
        private List<BackupFileInfo> backups = new List<BackupFileInfo>();
        private List<RestoreGroup> groups = new List<RestoreGroup>();
        private string? selectedZip;

        private RowListBox backupList = null!;
        private Label noBackups = null!;
        private RowListBox groupList = null!;
        private Label planMessage = null!;
        private Label resultLabel = null!;
        private FlatButton restoreButton = null!;

        public RestoreWindow(MainWindow owner)
        {
            this.owner = owner;

            Text = "Restore from a backup";
            ClientSize = new Size(780, 660);
            MinimumSize = new Size(700, 600);
            BackColor = Theme.Base;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            System.Drawing.Icon? appIcon = AppPaths.LoadIcon();
            if (appIcon != null) Icon = appIcon;
            Theme.StyleWindow(this);

            BuildUi();
            LoadBackups();
        }

        private static Label MakeLabel(string text, Font font, Color color, Color back)
        {
            return new Label { Text = text, Font = font, ForeColor = color, BackColor = back, AutoSize = true };
        }

        private void BuildUi()
        {
            // ---- bottom bar (docked first so it keeps its height)
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 104, BackColor = Theme.Base };
            Label info = new Label
            {
                Text = "Close the games first. Anything that would be replaced is copied into a safety zip before the restore, so you can undo it.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                BackColor = Theme.Base
            };
            resultLabel = new Label { Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Base, AutoEllipsis = true };
            restoreButton = new FlatButton { Text = "Restore selected", Kind = ButtonKind.Primary, Backdrop = Theme.Base, Enabled = false };
            restoreButton.Click += async (s, e) => await RestoreAsync();
            FlatButton closeButton = new FlatButton { Text = "Close", Kind = ButtonKind.Secondary, Backdrop = Theme.Base };
            closeButton.Click += (s, e) => Close();

            bottom.Controls.Add(info);
            bottom.Controls.Add(resultLabel);
            bottom.Controls.Add(restoreButton);
            bottom.Controls.Add(closeButton);
            bottom.Resize += (s, e) =>
            {
                int w = bottom.Width - 48;
                info.SetBounds(24, 6, w, 36);
                closeButton.SetBounds(bottom.Width - 24 - 90, 54, 90, 38);
                restoreButton.SetBounds(closeButton.Left - 10 - 200, 54, 200, 38);
                resultLabel.SetBounds(24, 60, Math.Max(40, restoreButton.Left - 24 - 16), 26);
            };

            // ---- body: two cards
            Panel body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Base, Padding = new Padding(24, 20, 24, 0) };
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Base,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(BuildBackupCard(), 0, 0);
            layout.Controls.Add(BuildGroupCard(), 0, 2);
            body.Controls.Add(layout);

            Controls.Add(body);
            Controls.Add(bottom);
        }

        private CardPanel BuildBackupCard()
        {
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };

            Label title = MakeLabel("Choose a backup", Theme.Heading, Theme.Text, Theme.Surface);
            FlatButton browse = new FlatButton { Text = "Browse for a zip", Kind = ButtonKind.Secondary, Height = 34 };
            browse.Click += (s, e) => BrowseForZip();

            backupList = new RowListBox { ItemHeight = 52 };
            backupList.DrawItem += BackupList_DrawItem;
            backupList.SelectedIndexChanged += (s, e) =>
            {
                int index = backupList.SelectedIndex;
                if (index >= 0 && index < backups.Count) LoadPlanFor(backups[index].Path);
            };

            noBackups = new Label
            {
                Text = "No backups found in your backup folder.\nUse Browse for a zip to pick one from somewhere else.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                BackColor = Theme.Surface,
                TextAlign = ContentAlignment.MiddleCenter
            };

            card.Controls.Add(title);
            card.Controls.Add(browse);
            card.Controls.Add(backupList);
            card.Controls.Add(noBackups);
            noBackups.BringToFront();
            card.Resize += (s, e) =>
            {
                title.Location = new Point(20, 16);
                browse.SetBounds(card.Width - 20 - 150, 12, 150, 34);
                backupList.SetBounds(10, 54, card.Width - 20, Math.Max(10, card.Height - 64));
                noBackups.SetBounds(30, 54, Math.Max(10, card.Width - 60), Math.Max(10, card.Height - 64));
            };
            return card;
        }

        private CardPanel BuildGroupCard()
        {
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };

            Label title = MakeLabel("What to restore", Theme.Heading, Theme.Text, Theme.Surface);
            planMessage = new Label { Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Surface, AutoEllipsis = true, Text = "Pick a backup above." };

            groupList = new RowListBox { ItemHeight = 62 };
            groupList.DrawItem += GroupList_DrawItem;
            groupList.MouseDown += (s, e) =>
            {
                int index = groupList.IndexFromPoint(e.Location);
                if (index < 0 || index >= groups.Count) return;
                RestoreGroup group = groups[index];
                if (!group.Allowed) return;
                group.Checked = !group.Checked;
                groupList.Invalidate();
                UpdateRestoreButton();
            };

            card.Controls.Add(title);
            card.Controls.Add(planMessage);
            card.Controls.Add(groupList);
            card.Resize += (s, e) =>
            {
                title.Location = new Point(20, 16);
                planMessage.SetBounds(20, 42, Math.Max(40, card.Width - 40), 18);
                groupList.SetBounds(10, 68, card.Width - 20, Math.Max(10, card.Height - 78));
            };
            return card;
        }

        // ------------------------------------------------------------------ backups

        private void LoadBackups()
        {
            backups = RestoreService.ListBackups(AppSettings.Current.BackupFolder);
            RefreshBackupList();
            if (backups.Count > 0) backupList.SelectedIndex = 0;   // newest backup is preselected
        }

        private void RefreshBackupList()
        {
            backupList.BeginUpdate();
            backupList.Items.Clear();
            foreach (BackupFileInfo backup in backups) backupList.Items.Add(backup);
            backupList.EndUpdate();
            noBackups.Visible = backups.Count == 0;
        }

        private void BrowseForZip()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose a backup zip";
                dialog.Filter = "Backup zip (*.zip)|*.zip";
                dialog.RestoreDirectory = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                FileInfo info = new FileInfo(dialog.FileName);
                backups.RemoveAll(b => string.Equals(b.Path, info.FullName, StringComparison.OrdinalIgnoreCase));
                backups.Insert(0, new BackupFileInfo { Path = info.FullName, Time = info.LastWriteTime, Size = info.Length });
                RefreshBackupList();
                backupList.SelectedIndex = 0;
            }
        }

        private void BackupList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= backups.Count) return;
            BackupFileInfo backup = backups[e.Index];
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool hover = backupList.HoverIndex == e.Index;
            Rectangle r = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 3, e.Bounds.Width - 4, e.Bounds.Height - 6);

            using (GraphicsPath path = Theme.RoundedRect(r, 10f))
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

            double megabytes = backup.Size / (1024.0 * 1024.0);
            Theme.DrawText(g, backup.Time.ToString("d MMM yyyy, HH:mm"), Theme.BodyStrong, Theme.Text, new Rectangle(r.X + 16, r.Y + 6, r.Width - 32, 20));
            Theme.DrawText(g, Path.GetFileName(backup.Path) + ", " + megabytes.ToString("0.0") + " MB", Theme.Small, Theme.Muted, new Rectangle(r.X + 16, r.Y + 26, r.Width - 32, 16));
        }

        // ------------------------------------------------------------------ what to restore

        private void LoadPlanFor(string zipPath)
        {
            selectedZip = zipPath;
            resultLabel.Text = "";
            List<RestoreGroup>? plan = RestoreService.LoadPlan(zipPath, owner.GetTrackedGames(), out string error);

            groups = plan ?? new List<RestoreGroup>();
            if (plan == null)
            {
                planMessage.Text = error;
                planMessage.ForeColor = Theme.Danger;
            }
            else
            {
                planMessage.Text = "Tick what you want back. Nothing changes until you press Restore.";
                planMessage.ForeColor = Theme.Muted;
            }

            groupList.BeginUpdate();
            groupList.Items.Clear();
            foreach (RestoreGroup group in groups) groupList.Items.Add(group.Title);
            groupList.EndUpdate();
            groupList.Invalidate();
            UpdateRestoreButton();
        }

        private void UpdateRestoreButton()
        {
            int count = groups.Count(g => g.Checked && g.Allowed);
            restoreButton.Enabled = count > 0;
            restoreButton.Text = count > 0 ? "Restore " + count + (count == 1 ? " item" : " items") : "Restore selected";
        }

        private void GroupList_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= groups.Count) return;
            RestoreGroup group = groups[e.Index];
            Graphics g = e.Graphics;
            bool hover = groupList.HoverIndex == e.Index;
            Rectangle r = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 3, e.Bounds.Width - 4, e.Bounds.Height - 6);

            using (GraphicsPath path = Theme.RoundedRect(r, 10f))
            {
                if (hover && group.Allowed)
                {
                    using (SolidBrush fill = new SolidBrush(Theme.Raised)) g.FillPath(fill, path);
                }
            }

            // checkbox
            Rectangle box = new Rectangle(r.X + 14, r.Y + (r.Height - 22) / 2, 22, 22);
            using (GraphicsPath boxPath = Theme.RoundedRect(box, 6f))
            {
                if (group.Checked && group.Allowed)
                {
                    using (SolidBrush fill = new SolidBrush(Theme.Accent)) g.FillPath(fill, boxPath);
                    using (Pen tick = new Pen(Theme.OnAccent, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(tick, new[]
                        {
                            new PointF(box.X + 5.5f, box.Y + 11.5f),
                            new PointF(box.X + 9.5f, box.Y + 15.5f),
                            new PointF(box.X + 16.5f, box.Y + 7f)
                        });
                    }
                }
                else
                {
                    using (Pen pen = new Pen(group.Allowed ? Theme.Muted : Theme.Border, 1.5f)) g.DrawPath(pen, boxPath);
                }
            }

            int statusWidth = 140;
            int textX = box.Right + 14;
            int textWidth = Math.Max(40, r.Right - textX - statusWidth - 16);
            Color titleColor = group.Allowed ? Theme.Text : Theme.Muted;
            Theme.DrawText(g, group.Title, Theme.BodyStrong, titleColor, new Rectangle(textX, r.Y + 8, textWidth, 22));
            Theme.DrawText(g, group.Detail, Theme.Small, Theme.Muted, new Rectangle(textX, r.Y + 31, textWidth, 18));

            Color statusColor = !group.Allowed ? Theme.Danger : group.Status.StartsWith("replaces") ? Theme.Accent : Theme.Muted;
            Theme.DrawText(g, group.Status, Theme.Small, statusColor, new Rectangle(r.Right - statusWidth - 14, r.Y, statusWidth, r.Height), TextFormatFlags.Right);
        }

        // ------------------------------------------------------------------ restoring

        private async Task RestoreAsync()
        {
            if (selectedZip == null) return;
            List<RestoreGroup> chosen = groups.Where(g => g.Checked && g.Allowed).ToList();
            if (chosen.Count == 0) return;

            int replaced = chosen.Sum(g => g.Files.Count(f => File.Exists(f.Target)));
            string question = "Restore " + chosen.Count + (chosen.Count == 1 ? " item" : " items") + " from this backup?\n\n" +
                              (replaced > 0 ? replaced + " existing files will be replaced. A safety copy of them is made first.\n\n" : "") +
                              "Make sure the games are closed.";
            if (MessageBox.Show(this, question, "Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            restoreButton.Enabled = false;
            resultLabel.ForeColor = Theme.Muted;
            resultLabel.Text = "Restoring...";

            string zipPath = selectedZip;
            RestoreResult result = await Task.Run(() => RestoreService.Apply(zipPath, chosen));

            // The game list and unlock history live in memory while the app runs, so the app applies them itself.
            try
            {
                if (result.GameListJson != null) owner.ApplyRestoredGameList(result.GameListJson);
                if (result.HistoryJson != null) UnlockHistory.ReplaceFromJson(result.HistoryJson);
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Problems.Add(ex.Message);
            }
            if (IsDisposed) return;

            string text = "Restored " + result.Restored + (result.Restored == 1 ? " file" : " files");
            if (result.Failed > 0) text += ", " + result.Failed + " failed";
            if (result.SafetyCopy.Length > 0) text += ". Safety copy: " + result.SafetyCopy;
            resultLabel.Text = text;
            resultLabel.ForeColor = result.Failed > 0 ? Theme.Danger : Theme.Good;

            if (result.Failed > 0)
            {
                planMessage.Text = "Some files couldn't be replaced. Close the game and try again. " + (result.Problems.Count > 0 ? result.Problems[0] : "");
                planMessage.ForeColor = Theme.Danger;
            }
            UpdateRestoreButton();
        }
    }
}
