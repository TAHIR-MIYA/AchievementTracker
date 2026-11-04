using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace AchievementTracker
{
    public class HistoryStatsResult
    {
        public int Total { get; set; }
        public int ThisWeek { get; set; }
        public UnlockEntry? Rarest { get; set; }
        public string TopGame { get; set; } = "";
        public int TopGameCount { get; set; }
        public int CurrentStreak { get; set; }   // days in a row with an unlock, counting back from today (or yesterday)
        public int LongestStreak { get; set; }
    }

    public static class HistoryStats
    {
        public static HistoryStatsResult Compute(List<UnlockEntry> entries, DateTime now)
        {
            HistoryStatsResult result = new HistoryStatsResult { Total = entries.Count };
            if (entries.Count == 0) return result;

            DateTime weekAgoUtc = now.ToUniversalTime().AddDays(-7);
            result.ThisWeek = entries.Count(entry => entry.TimeUtc > weekAgoUtc);

            result.Rarest = entries.Where(entry => entry.Percent.HasValue).OrderBy(entry => entry.Percent!.Value).FirstOrDefault();

            var topGame = entries.GroupBy(entry => entry.AppId).OrderByDescending(group => group.Count()).First();
            result.TopGame = topGame.OrderByDescending(entry => entry.TimeUtc).First().GameName;
            result.TopGameCount = topGame.Count();

            // Streaks work on local calendar days
            HashSet<DateTime> days = new HashSet<DateTime>(entries.Select(entry => entry.TimeUtc.ToLocalTime().Date));

            DateTime today = now.Date;
            DateTime cursor = days.Contains(today) ? today : days.Contains(today.AddDays(-1)) ? today.AddDays(-1) : DateTime.MinValue;
            if (cursor != DateTime.MinValue)
            {
                while (days.Contains(cursor))
                {
                    result.CurrentStreak++;
                    cursor = cursor.AddDays(-1);
                }
            }

            int run = 0;
            DateTime? previous = null;
            foreach (DateTime day in days.OrderBy(d => d))
            {
                run = previous.HasValue && day == previous.Value.AddDays(1) ? run + 1 : 1;
                if (run > result.LongestStreak) result.LongestStreak = run;
                previous = day;
            }
            return result;
        }
    }

    public static class HistoryExport
    {
        /// <summary>Writes the history as a CSV file that Excel opens correctly (UTF-8 with a BOM).</summary>
        public static void WriteCsv(List<UnlockEntry> entries, string path)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Date,Time,Game,App ID,Achievement,Rarity %,Rarity,Screenshot");

            foreach (UnlockEntry entry in entries.OrderByDescending(e => e.TimeUtc))
            {
                DateTime local = entry.TimeUtc.ToLocalTime();
                text.AppendLine(string.Join(",",
                    Csv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(entry.GameName),
                    Csv(entry.AppId),
                    Csv(entry.Name),
                    Csv(entry.Percent.HasValue ? entry.Percent.Value.ToString("0.0", CultureInfo.InvariantCulture) : ""),
                    Csv(entry.Percent.HasValue ? Rarity.Tier(entry.Percent.Value) : ""),
                    Csv(entry.Screenshot ?? "")));
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        }

        private static string Csv(string value)
        {
            // A cell that starts with = + - or @ would be run as a formula by Excel, so mark it as plain text.
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0) value = "'" + value;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) value = "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}
