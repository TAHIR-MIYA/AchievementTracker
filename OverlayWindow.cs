using System;
using System.Drawing;
using System.Windows.Forms;
using System.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;

namespace AchievementTracker
{
    public class OverlayWindow : Form
    {
        // 1. Audio API
        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, string returnString, int returnLength, IntPtr hwndCallback);

        // 2. Rounded Corners API (PS5 Style)
        [DllImport("Gdi32.dll", EntryPoint = "CreateRoundRectRgn")]
        private static extern IntPtr CreateRoundRectRgn(
            int nLeftRect,     
            int nTopRect,      
            int nRightRect,    
            int nBottomRect,   
            int nWidthEllipse, 
            int nHeightEllipse 
        );

        private System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
        private int targetY;
        private int currentY;
        private int animationState = 0; 
        private int holdCounter = 0;

        public OverlayWindow(string achievementName, string iconUrl)
        {
            this.FormBorderStyle = FormBorderStyle.None; 
            this.TopMost = true;                         
            this.ShowInTaskbar = false;                  
            this.StartPosition = FormStartPosition.Manual;

            // PS5 Dark Theme
            this.BackColor = Color.FromArgb(20, 20, 20);
            this.ForeColor = Color.White;
            this.Size = new Size(340, 75);
            
            // Apply PS5-style rounded corners
            this.Region = System.Drawing.Region.FromHrgn(CreateRoundRectRgn(0, 0, Width, Height, 25, 25));

            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                 this.Close();
                 return;
            }

            // PS5 Positioning (Top Right)
            int screenWidth = screen.Bounds.Width;
            targetY = 40;  // Final resting place (40px from top)
            currentY = -100; // Start hidden above the screen
            this.Location = new Point(screenWidth - this.Width - 40, currentY);

            // PS5 Layout
            PictureBox iconBox = new PictureBox
            {
                Size = new Size(45, 45),
                Location = new Point(15, 15),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.FromArgb(30, 30, 30) // Dark placeholder
            };
            this.Controls.Add(iconBox);

            Label topLabel = new Label
            {
                Text = "Trophy earned!",
                Font = new Font("Segoe UI", 9, FontStyle.Regular),
                Location = new Point(70, 15),
                AutoSize = true,
                ForeColor = Color.LightGray
            };
            this.Controls.Add(topLabel);

            Label nameLabel = new Label
            {
                Text = achievementName,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                Location = new Point(70, 35),
                AutoSize = true,
                ForeColor = Color.White
            };
            this.Controls.Add(nameLabel);

            // Fetch the image
            if (!string.IsNullOrEmpty(iconUrl))
            {
                _ = LoadIconAsync(iconUrl, iconBox);
            }

            PlayNotificationSound();

            // Setup smooth animation
            animationTimer.Interval = 15; 
            animationTimer.Tick += AnimationTick; 
            animationTimer.Start();
        }

        private async Task LoadIconAsync(string url, PictureBox box)
        {
            try
            {
                using HttpClient client = new HttpClient();
                byte[] imageBytes = await client.GetByteArrayAsync(url);
                using MemoryStream ms = new MemoryStream(imageBytes);
                Image img = Image.FromStream(ms);
                
                this.Invoke(new Action(() => {
                    box.Image = img;
                    box.BackColor = Color.Transparent; 
                }));
            }
            catch { }
        }

        private void AnimationTick(object? sender, EventArgs e)
        {
            // State 0: Sliding DOWN from the top
            if (animationState == 0) 
            {
                currentY += 10; // Speed of slide
                if (currentY >= targetY)
                {
                    currentY = targetY;
                    animationState = 1; 
                }
                this.Location = new Point(this.Location.X, currentY);
            }
            // State 1: Holding on screen
            else if (animationState == 1) 
            {
                holdCounter += 15;
                if (holdCounter >= 5000) // Hold for 5 seconds
                {
                    animationState = 2; 
                }
            }
            // State 2: Sliding UP off the screen
            else if (animationState == 2) 
            {
                currentY -= 10; 
                this.Location = new Point(this.Location.X, currentY);

                if (currentY <= -100) 
                {
                    animationTimer.Stop();
                    this.Close(); 
                }
            }
        }

        private void PlayNotificationSound()
        {
            try 
            {
                if (File.Exists("unlock.mp3"))
                {
                    mciSendString("close unlockSound", null, 0, IntPtr.Zero); 
                    mciSendString("open \"unlock.mp3\" type mpegvideo alias unlockSound", null, 0, IntPtr.Zero);
                    mciSendString("play unlockSound", null, 0, IntPtr.Zero);
                }
                else if (File.Exists("unlock.wav"))
                {
                    using (SoundPlayer player = new SoundPlayer("unlock.wav"))
                    {
                        player.Play();
                    }
                }
                else
                {
                    SystemSounds.Exclamation.Play(); 
                }
            } 
            catch { }
        }
    }
}