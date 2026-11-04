using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AchievementTracker
{
    public class AchievementState
    {
        public bool Unlocked { get; set; }
        public DateTime? Time { get; set; }   // when it was unlocked (local time), if the file says
    }

    /// <summary>
    /// Reads emulator achievement .ini files (CODEX, RUNE, FLT, TENOKE, OnlineFix style).
    ///
    ///   [ACH04]                 one block per achievement; "Achieved=1" means unlocked
    ///   Achieved=1              (CurProgress and MaxProgress are NOT achievements)
    ///   UnlockTime=1785042038   when it was unlocked (Unix seconds)
    ///   ...
    ///   [SteamAchievements]     index list + "Count=N" (also not achievements)
    ///   00000=ACH04
    ///   Count=1
    ///
    /// A flat style (NAME=1 inside an "achievements" section) is supported too.
    /// </summary>
    public static class AchievementIni
    {
        // Keys that hold numbers or timestamps but never mean "this achievement is unlocked".
        private static readonly HashSet<string> IgnoredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "count", "curprogress", "maxprogress", "currentprogress", "unlocktime", "time", "progress", "version"
        };

        /// <summary>Returns every achievement found in the file and whether it is unlocked.</summary>
        public static Dictionary<string, bool> Read(string path)
        {
            return ReadWithTimes(path).ToDictionary(entry => entry.Key, entry => entry.Value.Unlocked, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Like Read, but also returns the unlock time of each unlocked achievement when the file has one.</summary>
        public static Dictionary<string, AchievementState> ReadWithTimes(string path)
        {
            Dictionary<string, AchievementState> result = new Dictionary<string, AchievementState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, DateTime> times = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs))
            {
                string section = "";
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim();
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq <= 0 || section.Length == 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string rawValue = line.Substring(eq + 1).Trim();

                    if (key.Equals("unlocktime", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime? time = ParseUnixTime(rawValue);
                        if (time.HasValue) times[section] = time.Value;
                        continue;
                    }

                    bool? state = ParseBool(rawValue);
                    if (state == null) continue;

                    if (key.Equals("achieved", StringComparison.OrdinalIgnoreCase))
                    {
                        result[section] = new AchievementState { Unlocked = state.Value };   // block style: the section name is the achievement
                    }
                    else if (!IgnoredKeys.Contains(key) && !key.All(char.IsDigit) &&
                             section.IndexOf("achievement", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result[key] = new AchievementState { Unlocked = state.Value };       // flat style: NAME=1
                    }
                }
            }

            // UnlockTime can come after Achieved in the block, so attach the times at the end.
            foreach (KeyValuePair<string, DateTime> entry in times)
            {
                if (result.TryGetValue(entry.Key, out AchievementState? existing) && existing.Unlocked) existing.Time = entry.Value;
            }
            return result;
        }

        private static bool? ParseBool(string value)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        private static DateTime? ParseUnixTime(string value)
        {
            if (!long.TryParse(value, out long seconds) || seconds <= 0) return null;
            if (seconds > 100000000000L) seconds /= 1000;   // some tools write milliseconds
            try
            {
                DateTime time = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                return time.Year >= 2000 && time.Year <= 2100 ? time : (DateTime?)null;
            }
            catch
            {
                return null;
            }
        }
    }
}
