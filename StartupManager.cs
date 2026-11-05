using System;
using System.IO;
using Microsoft.Win32;

namespace AchievementTracker
{
    /// <summary>
    /// "Start with Windows": adds or removes this app in the current user's Run list
    /// (HKEY_CURRENT_USER, no administrator rights needed). It starts with --minimized, so it sits in the tray.
    /// </summary>
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AchievementTracker";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key?.GetValue(ValueName) is string command && command.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (enabled)
                    {
                        if (!TryBuildCommand(out string command, out error)) return false;
                        key.SetValue(ValueName, command);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Couldn't change the startup setting: " + ex.Message;
                return false;
            }
        }

        /// <summary>If it is switched on but the .exe has moved or been rebuilt elsewhere, point the entry at the current .exe.</summary>
        public static void RefreshPathIfEnabled()
        {
            try
            {
                if (!IsEnabled()) return;
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string? registered = key?.GetValue(ValueName) as string;
                    if (TryBuildCommand(out string expected, out _) && !string.Equals(registered, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        SetEnabled(true, out _);
                    }
                }
            }
            catch { }
        }

        private static bool TryBuildCommand(out string command, out string error)
        {
            command = "";
            error = "";

            string? exe = Environment.ProcessPath;
            // Started with "dotnet run"? Then the process is dotnet.exe, so use the app's own .exe next to the .dll.
            if (string.IsNullOrEmpty(exe) || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                exe = Path.Combine(AppContext.BaseDirectory, "AchievementTracker.exe");
            }
            if (!File.Exists(exe))
            {
                error = "Couldn't find AchievementTracker.exe to start at login. Build or publish the app first.";
                return false;
            }

            command = "\"" + exe + "\" --minimized";
            return true;
        }
    }
}
