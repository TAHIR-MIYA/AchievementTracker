using System;
using System.Drawing;
using System.Windows.Forms;
using System.Media; 

namespace AchievementTracker
{
    public class OverlayWindow : Form
    {
        private System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
        private int targetY;
        private int currentY;
        private int animationState = 0; 
        private int holdCounter = 0;

        public OverlayWindow(string achievementName)
        {
            this.FormBorderStyle = FormBorderStyle.None; 
            this.TopMost = true;                         
            this.ShowInTaskbar = false;                  
            this.StartPosition = FormStartPosition.Manual;

            this.BackColor = Color.Magenta;
            this.TransparencyKey = Color.Magenta;

            // Handle potential null from Screen.PrimaryScreen
            var screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                 // Fallback if no screen detected
                 this.Close();
                 return;
            }

            int screenWidth = screen.Bounds.Width;
            int screenHeight = screen.Bounds.Height;
            this.Size = new Size(350, 80);
            
            targetY = screenHeight - 150; 
            currentY = screenHeight;      
            this.Location = new Point((screenWidth - this.Width) / 2, currentY);

            Panel notificationBox = new Panel
            {
                Size = new Size(350, 80),
                BackColor = Color.FromArgb(30, 30, 30), 
                ForeColor = Color.White
            };
            
            Panel accentStripe = new Panel { Size = new Size(5, 80), BackColor = Color.DeepSkyBlue, Dock = DockStyle.Left };
            notificationBox.Controls.Add(accentStripe);

            Label titleLabel = new Label
            {
                Text = "🏆 Achievement Unlocked!",
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };
            notificationBox.Controls.Add(titleLabel);

            Label nameLabel = new Label
            {
                Text = achievementName,
                Font = new Font("Segoe UI", 12, FontStyle.Regular),
                Location = new Point(20, 40),
                AutoSize = true,
                ForeColor = Color.LightGray
            };
            notificationBox.Controls.Add(nameLabel);

            this.Controls.Add(notificationBox);

            PlayNotificationSound();

            animationTimer.Interval = 15; 
            animationTimer.Tick += AnimationTick; 
            animationTimer.Start();
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
                    Application.ExitThread(); // Added to properly close the thread we created in TrackerEngine
                }
            }
        }

        private void PlayNotificationSound()
        {
            try 
            {
                SystemSounds.Exclamation.Play(); 
            } 
            catch { }
        }
    }
}