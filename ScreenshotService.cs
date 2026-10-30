using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AchievementTracker
{
    /// <summary>Saves a screenshot of the monitor the game is on, when an achievement unlocks.</summary>
    public static class ScreenshotService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        // Lets this thread see real pixels on scaled (125%, 150%...) displays, so screenshots are full resolution.
        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        private static readonly IntPtr PerMonitorAwareV2 = new IntPtr(-4);

        public static string FolderPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "AchievementTracker");

        /// <summary>Returns the saved file's path, or null if the screenshot failed.</summary>
        public static string? Capture(string gameName, string achievementName)
        {
            IntPtr previousContext = IntPtr.Zero;
            bool changedContext = false;
            try
            {
                try
                {
                    previousContext = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
                    changedContext = previousContext != IntPtr.Zero;
                }
                catch { }

                IntPtr foreground = GetForegroundWindow();
                Screen screen = foreground != IntPtr.Zero ? Screen.FromHandle(foreground) : (Screen.PrimaryScreen ?? Screen.AllScreens[0]);
                Rectangle bounds = screen.Bounds;

                string folder = Path.Combine(FolderPath, SafeName(gameName));
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, SafeName(achievementName) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".jpg");

                using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    bitmap.Save(file, ImageFormat.Jpeg);
                }
                return file;
            }
            catch
            {
                return null;
            }
            finally
            {
                try { if (changedContext) SetThreadDpiAwarenessContext(previousContext); } catch { }
            }
        }

        private static string SafeName(string text)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) text = text.Replace(c, '_');
            text = text.Trim();
            if (text.Length == 0) return "unknown";
            return text.Length > 60 ? text.Substring(0, 60) : text;
        }
    }
}
