using System;
using System.Drawing;
using System.Windows.Forms;
using System.Media;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices; // <-- ADD THIS LINE

namespace AchievementTracker
{
    public class OverlayWindow : Form
    {
        // <-- ADD THIS SPECIAL WINDOWS MEDIA PLAYER CODE -->
        [DllImport("winmm.dll")]
        private static extern long mciSendString(string command, string returnString, int returnLength, IntPtr hwndCallback);

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

            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;

            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                 this.Close();
                 return;
            }

            int screenWidth = screen.Bounds.Width;
            int screenHeight = screen.Bounds.Height;
            this.Size = new Size(380, 80);
            
            targetY = screenHeight - 160; 
            currentY = screenHeight;      
            this.Location = new Point((screenWidth - this.Width) / 2, currentY);

            Panel notificationBox = new Panel
            {
                Size = new Size(380, 80),
                BackColor = Color.FromArgb(30, 30, 30), 
                ForeColor = Color.White
            };
            
            Panel accentStripe = new Panel { Size = new Size(5, 80), BackColor = Color.DeepSkyBlue, Dock = DockStyle.Left };
            notificationBox.Controls.Add(accentStripe);

            // The New Image Box
            PictureBox iconBox = new PictureBox
            {
                Size = new Size(50, 50),
                Location = new Point(20, 15),
                SizeMode = PictureBoxSizeMode.StretchImage,
                BackColor = Color.FromArgb(45, 45, 48) // Placeholder color
            };
            notificationBox.Controls.Add(iconBox);

            Label titleLabel = new Label
            {
                Text = "🏆 Achievement Unlocked!",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(80, 15),
                AutoSize = true
            };
            notificationBox.Controls.Add(titleLabel);

            Label nameLabel = new Label
            {
                Text = achievementName,
                Font = new Font("Segoe UI", 12, FontStyle.Regular),
                Location = new Point(80, 40),
                AutoSize = true,
                ForeColor = Color.LightGray
            };
            notificationBox.Controls.Add(nameLabel);

            this.Controls.Add(notificationBox);

            // Fetch the image if a URL was provided
            if (!string.IsNullOrEmpty(iconUrl))
            {
                _ = LoadIconAsync(iconUrl, iconBox);
            }

            PlayNotificationSound();

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
            if (animationState == 0) 
            {
                currentY -= 12; 
                if (currentY <= targetY)
                {
                    currentY = targetY;
                    animationState = 1; 
                }
                this.Location = new Point(this.Location.X, currentY);
            }
            else if (animationState == 1) 
            {
                holdCounter += 15;
                if (holdCounter >= 5000) 
                {
                    animationState = 2; 
                }
            }
            else if (animationState == 2) 
            {
                currentY += 12; 
                this.Location = new Point(this.Location.X, currentY);
                
                var screen = Screen.PrimaryScreen;
                int screenHeight = screen != null ? screen.Bounds.Height : 1080;

                if (currentY >= screenHeight) 
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
                // 1. Check for MP3 first!
                if (File.Exists("unlock.mp3"))
                {
                    mciSendString("close unlockSound", null, 0, IntPtr.Zero); 
                    mciSendString("open \"unlock.mp3\" type mpegvideo alias unlockSound", null, 0, IntPtr.Zero);
                    mciSendString("play unlockSound", null, 0, IntPtr.Zero);
                }
                // 2. Fallback to WAV
                else if (File.Exists("unlock.wav"))
                {
                    using (SoundPlayer player = new SoundPlayer("unlock.wav"))
                    {
                        player.Play();
                    }
                }
                // 3. Fallback to Windows default ding
                else
                {
                    SystemSounds.Exclamation.Play(); 
                }
            } 
            catch { }
        }
    }
}