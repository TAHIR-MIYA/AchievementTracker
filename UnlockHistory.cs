using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AchievementTracker
{
    public class UnlockEntry
    {
        public DateTime TimeUtc { get; set; }
        public string AppId { get; set; } = "";
        public string GameName { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public double? Percent { get; set; }
        public string? Screenshot { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Every unlock the tracker sees, saved to %APPDATA%\AchievementTracker\unlock_history.json
    /// (the newest 500 are kept).
    /// </summary>
    public static class UnlockHistory
    {
        private const int MaxEntries = 500;
        private static readonly object Gate = new object();
        private static List<UnlockEntry> entries = Load();

        public static event EventHandler? Changed;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AchievementTracker", "unlock_history.json");

        private static List<UnlockEntry> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<List<UnlockEntry>>(File.ReadAllText(FilePath)) ?? new List<UnlockEntry>();
                }
            }
            catch { }
            return new List<UnlockEntry>();
        }

        /// <summary>A copy of the history, newest first.</summary>
        public static List<UnlockEntry> Snapshot()
        {
            lock (Gate)
            {
                return entries.OrderByDescending(e => e.TimeUtc).ToList();
            }
        }

        public static void Add(UnlockEntry entry)
        {
            lock (Gate)
            {
                entries.Add(entry);
                if (entries.Count > MaxEntries)
                {
                    entries = entries.OrderByDescending(e => e.TimeUtc).Take(MaxEntries).ToList();
                }
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static void Clear()
        {
            lock (Gate)
            {
                entries = new List<UnlockEntry>();
                Save();
            }
            Changed?.Invoke(null, EventArgs.Empty);
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
