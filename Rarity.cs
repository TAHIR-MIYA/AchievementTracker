using System.Globalization;

namespace AchievementTracker
{
    /// <summary>Turns Steam's "% of players who unlocked it" into a rarity tier and a short label.</summary>
    public static class Rarity
    {
        public static string Tier(double percent)
        {
            if (percent < 5) return "Ultra rare";
            if (percent < 20) return "Rare";
            if (percent < 50) return "Uncommon";
            return "Common";
        }

        // Rare and ultra rare get the gold highlight, the others stay quiet.
        public static bool IsHighlighted(double percent) => percent < 20;

        public static string FormatPercent(double percent)
        {
            string number = percent < 10
                ? percent.ToString("0.0", CultureInfo.InvariantCulture)
                : percent.ToString("0", CultureInfo.InvariantCulture);
            return number + "%";
        }

        public static string Label(double percent) => Tier(percent) + " " + FormatPercent(percent);
    }
}
