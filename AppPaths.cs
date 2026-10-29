using System;
using System.Drawing;
using System.IO;

namespace AchievementTracker
{
    /// <summary>
    /// Everything the app ships with (sound, icon, json data) is located next to the .exe,
    /// never relative to the "current folder" (which changes when a file dialog is used or
    /// when the exe is started from a shortcut or at login).
    /// </summary>
    public static class AppPaths
    {
        public static string InBase(string fileName) => Path.Combine(AppContext.BaseDirectory, fileName);

        /// <summary>Finds a bundled file next to the exe (falls back to the current folder). Null if missing.</summary>
        public static string? FindAsset(string fileName)
        {
            string a = InBase(fileName);
            if (File.Exists(a)) return a;
            string b = Path.Combine(Environment.CurrentDirectory, fileName);
            return File.Exists(b) ? b : null;
        }

        /// <summary>
        /// The app icon. Uses app_icon.ico if it is next to the exe; otherwise reads the icon that is
        /// embedded in the .exe itself, so the icon still shows even if the .ico file is missing.
        /// </summary>
        public static Icon? LoadIcon()
        {
            try
            {
                string? file = FindAsset("app_icon.ico");
                if (file != null) return new Icon(file);
            }
            catch { }

            try
            {
                string? exe = Environment.ProcessPath;
                if (exe != null) return Icon.ExtractAssociatedIcon(exe);
            }
            catch { }
            return null;
        }
    }
}
