using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class OverlayWindow : Form
    {
        // --- NATIVE OS HOOKS FOR Z-ORDERING, MONITOR LOOKUP & AUDIO ---
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, StringBuilder? returnString, int returnLength, IntPtr hwndCallback);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        private const int CardWidth = 420;
        private const int CardHeight = 84;
        private const int CornerRadius = 14;
        private const int ScreenMargin = 40;
        private const int SlideInMs = 450;
        private const int HoldMs = 4500;
        private const int SlideOutMs = 400;

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // --- WINDOW STYLES ---
        // WS_EX_NOACTIVATE: never steals focus from the game.
        // WS_EX_TOPMOST:    stays above the game window.
        // No WS_EX_TOOLWINDOW (capture tools skip tool windows) and no Opacity (it makes the form a
        // layered window that legacy capture methods can't see). The popup slides instead of fading.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        // Let mouse clicks fall through to the game underneath the popup.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }

        private readonly string _achievementName;
        private readonly string _iconUrl;
        private Image? _icon;
        private readonly System.Windows.Forms.Timer _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
        private readonly Stopwatch _clock = new Stopwatch();
        private int _left;
        private int _shownTop;
        private int _hiddenTop;

        public OverlayWindow(string achievementName, string iconUrl)
        {
            _achievementName = achievementName;
            _iconUrl = iconUrl;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            this.Text = "AchievementTracker Overlay"; // stable title so capture tools can find it
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Theme.Surface;
            this.Size = new Size(CardWidth, CardHeight);

            // Rounded corners: built once.
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0, 0, CardWidth, CardHeight), CornerRadius))
            {
                this.Region = new Region(path);
            }

            // Fallback icon (used until / unless the Steam icon downloads).
            try
            {
                System.Drawing.Icon? appIcon = AppPaths.LoadIcon();
                if (appIcon != null) _icon = new System.Drawing.Icon(appIcon, new Size(64, 64)).ToBitmap();
            }
            catch { }

            _animTimer.Tick += AnimTimer_Tick;
            this.Load += OverlayWindow_Load;
        }

        private async Task LoadRemoteIconAsync()
        {
            try
            {
                byte[] bytes = await Http.GetByteArrayAsync(_iconUrl).ConfigureAwait(false);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms))
                {
                    Bitmap copy = new Bitmap(tmp);   // copy so we don't depend on the stream staying open
                    if (IsDisposed || !IsHandleCreated) { copy.Dispose(); return; }
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed) { copy.Dispose(); return; }
                        Image? old = _icon;
                        _icon = copy;
                        old?.Dispose();
                        Invalidate();
                    }));
                }
            }
            catch { }
        }

        // Show the popup on the monitor the game is running on, not always the primary one.
        private static Screen GetTargetScreen()
        {
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero) return Screen.FromHandle(fg);
            }
            catch { }
            return Screen.PrimaryScreen ?? Screen.AllScreens[0];
        }

        private void OverlayWindow_Load(object? sender, EventArgs e)
        {
            Rectangle area = GetTargetScreen().WorkingArea;
            _left = area.Right - this.Width - ScreenMargin;      // top-right corner of that monitor
            _shownTop = area.Top + ScreenMargin;
            _hiddenTop = area.Top - this.Height;                 // fully above the screen edge
            this.Location = new Point(_left, _hiddenTop);

            // Force native topmost override (without activating, so the game keeps focus)
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

            if (!string.IsNullOrEmpty(_iconUrl) && _iconUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                _ = LoadRemoteIconAsync();

            PlaySound();
            _clock.Restart();
            _animTimer.Start();
        }

        private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);
        private static double EaseInCubic(double x) => x * x * x;

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            long t = _clock.ElapsedMilliseconds;
            double top;

            if (t < SlideInMs)
                top = _hiddenTop + (_shownTop - _hiddenTop) * EaseOutCubic((double)t / SlideInMs);
            else if (t < SlideInMs + HoldMs)
                top = _shownTop;
            else if (t < SlideInMs + HoldMs + SlideOutMs)
                top = _shownTop + (_hiddenTop - _shownTop) * EaseInCubic((double)(t - SlideInMs - HoldMs) / SlideOutMs);
            else
            {
                _animTimer.Stop();
                this.Close(); // ends Application.Run for this popup so the next queued one can show
                return;
            }

            this.Top = (int)Math.Round(top);
            Invalidate();   // redraws the draining time bar
        }

        private void PlaySound()
        {
            int volume = AppSettings.Current.SoundVolume;
            if (volume <= 0) return;

            // The sound lives next to the exe (copied there by the build)
            string? audioPath = AppPaths.FindAsset("unlock.mp3");
            if (audioPath != null)
            {
                // Close any stuck audio streams first, then play (MCI volume is 0-1000)
                mciSendString("close unlockAudio", null, 0, IntPtr.Zero);
                mciSendString($"open \"{audioPath}\" type mpegvideo alias unlockAudio", null, 0, IntPtr.Zero);
                mciSendString($"setaudio unlockAudio volume to {volume * 10}", null, 0, IntPtr.Zero);
                mciSendString("play unlockAudio from 0", null, 0, IntPtr.Zero);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            Rectangle bounds = new Rectangle(0, 0, Width, Height);

            using (LinearGradientBrush bg = new LinearGradientBrush(bounds, Theme.Raised, Theme.Surface, 90f))
            {
                g.FillRectangle(bg, bounds);
            }

            // Icon (rounded)
            Rectangle iconRect = new Rectangle(16, (Height - 52) / 2, 52, 52);
            bool drewIcon = false;
            Image? icon = _icon;
            if (icon != null)
            {
                using (GraphicsPath clip = Theme.RoundedRect(iconRect, 10f))
                {
                    GraphicsState state = g.Save();
                    try
                    {
                        g.SetClip(clip);
                        g.DrawImage(icon, iconRect);
                        drewIcon = true;
                    }
                    catch { _icon = null; }
                    finally { g.Restore(state); }
                }
            }
            if (!drewIcon)
            {
                using (GraphicsPath p = Theme.RoundedRect(iconRect, 10f))
                using (SolidBrush b = new SolidBrush(Theme.Hover))
                {
                    g.FillPath(b, p);
                }
            }

            // Text
            Theme.DrawText(g, "Achievement unlocked", Theme.Small, Theme.Accent, new Rectangle(84, 16, Width - 84 - 16, 18));
            Theme.DrawText(g, _achievementName, Theme.Heading, Theme.Text, new Rectangle(84, 34, Width - 84 - 16, 28));

            // Time bar: drains while the popup is on screen
            long t = _clock.ElapsedMilliseconds;
            double remaining = 1.0 - Math.Max(0.0, Math.Min(1.0, (double)(t - SlideInMs) / HoldMs));
            int barWidth = (int)((Width - 40) * remaining);
            if (barWidth > 1)
            {
                using (GraphicsPath bar = Theme.RoundedRect(new RectangleF(20, Height - 9, barWidth, 3), 1.5f))
                using (SolidBrush accent = new SolidBrush(Theme.Accent))
                {
                    g.FillPath(accent, bar);
                }
            }

            // Soft anti-aliased border
            using (GraphicsPath outline = Theme.RoundedRect(new RectangleF(1, 1, Width - 2, Height - 2), CornerRadius - 1))
            using (Pen pen = new Pen(Theme.Border, 2f))
            {
                g.DrawPath(pen, outline);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animTimer.Dispose();
            _icon?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
