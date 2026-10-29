using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AchievementTracker
{
    /// <summary>
    /// Reads emulator achievement .ini files (CODEX, RUNE, FLT, TENOKE, OnlineFix style).
    ///
    ///   [ACH04]                 one block per achievement; "Achieved=1" means unlocked
    ///   Achieved=1              (CurProgress, MaxProgress and UnlockTime are NOT achievements)
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
            Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

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
                    bool? state = ParseBool(line.Substring(eq + 1).Trim());
                    if (state == null) continue;

                    if (key.Equals("achieved", StringComparison.OrdinalIgnoreCase))
                    {
                        result[section] = state.Value;                     // block style: the section name is the achievement
                    }
                    else if (!IgnoredKeys.Contains(key) && !key.All(char.IsDigit) &&
                             section.IndexOf("achievement", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result[key] = state.Value;                         // flat style: NAME=1
                    }
                }
            }
            return result;
        }

        private static bool? ParseBool(string value)
        {
            if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }
    }
}
