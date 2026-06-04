using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AchievementTracker
{
    public class OverlayWindow : Form
    {
        // --- NATIVE OS HOOKS FOR Z-ORDERING & AUDIO ---
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        
        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, StringBuilder? returnString, int returnLength, IntPtr hwndCallback);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // --- ANTI-FOCUS STEALING FLAGS ---
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (Prevents game controller interruption)
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW (Hides from Alt-Tab menu)
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST (Hardware level top priority)
                return cp;
            }
        }

        private string _achievementName;
        private string _iconUrl;
        private System.Windows.Forms.Timer _animTimer = new System.Windows.Forms.Timer { Interval = 30 };
        private int _ticks = 0;

        public OverlayWindow(string achievementName, string iconUrl)
        {
            _achievementName = achievementName;
            _iconUrl = iconUrl;
            this.DoubleBuffered = true; 
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(420, 80);
            this.BackColor = Color.FromArgb(25, 25, 25);
            this.ForeColor = Color.White;
            this.ShowInTaskbar = false;
            this.Opacity = 0;
            this.StartPosition = FormStartPosition.Manual;

            // Steam Icon Fetcher
            PictureBox iconBox = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(15, 15),
                SizeMode = PictureBoxSizeMode.Zoom
            };
            
            string localIcon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_icon.ico");

            // If we have a Steam image URL, load it asynchronously. Otherwise fallback to your default icon.
            if (!string.IsNullOrEmpty(_iconUrl) && _iconUrl.StartsWith("http"))
            {
                iconBox.LoadAsync(_iconUrl);
                // If Steam server fails, fallback safely
                iconBox.LoadCompleted += (s, e) => {
                    if (e.Error != null && File.Exists(localIcon)) iconBox.Image = new Icon(localIcon).ToBitmap();
                };
            }
            else if (File.Exists(localIcon))
            {
                iconBox.Image = new Icon(localIcon).ToBitmap();
            }

            Label titleLabel = new Label
            {
                Text = "Achievement Unlocked!",
                Font = new Font("Segoe UI Semibold", 9, FontStyle.Regular),
                ForeColor = Color.DarkGray,
                Location = new Point(80, 15),
                AutoSize = true
            };

            Label nameLabel = new Label
            {
                Text = _achievementName,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Location = new Point(80, 35),
                AutoSize = true
            };

            this.Controls.Add(iconBox);
            this.Controls.Add(titleLabel);
            this.Controls.Add(nameLabel);

            _animTimer.Tick += AnimTimer_Tick;

            this.Load += OverlayWindow_Load;
            this.Paint += OverlayWindow_Paint;
        }

        private void OverlayWindow_Load(object? sender, EventArgs e)
        {
            // TRUE PS5 POSITIONING (Top-Right)
            var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            this.Left = screen.Width - this.Width - 40; // 40 pixels padding from the right edge
            this.Top = 40; // 40 pixels padding from the top

            // Force native topmost override
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            PlaySound();
            _animTimer.Start();
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            _ticks++;
            if (_ticks < 15 && this.Opacity < 1.0) this.Opacity += 0.1;
            else if (_ticks > 150) // Approx 4.5 seconds
            {
                this.Opacity -= 0.1;
                if (this.Opacity <= 0)
                {
                    _animTimer.Stop();
                    this.Close(); // Destroys the form and ends the STA thread cleanly
                }
            }
        }

        private void PlaySound()
        {
            // Smart Pathing: Check the hidden build folder first, then check the outer VS Code folder!
            string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "unlock.mp3");
            if (!File.Exists(audioPath)) 
            {
                audioPath = Path.Combine(Environment.CurrentDirectory, "unlock.mp3");
            }

            if (File.Exists(audioPath))
            {
                // Close any stuck audio streams first, then play
                mciSendString("close unlockAudio", null, 0, IntPtr.Zero);
                mciSendString($"open \"{audioPath}\" type mpegvideo alias unlockAudio", null, 0, IntPtr.Zero);
                mciSendString("play unlockAudio from 0", null, 0, IntPtr.Zero);
            }
        }

        // Draw PS5 style rounded corners
        private void OverlayWindow_Paint(object? sender, PaintEventArgs e)
        {
            GraphicsPath path = new GraphicsPath();
            int r = 15;
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(this.Width - r, 0, r, r, 270, 90);
            path.AddArc(this.Width - r, this.Height - r, r, r, 0, 90);
            path.AddArc(0, this.Height - r, r, r, 90, 90);
            path.CloseFigure();
            this.Region = new Region(path);
        }
        
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }
    }
}