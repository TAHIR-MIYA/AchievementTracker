namespace AchievementTracker
{
    /// <summary>Everything a popup needs to show: a normal unlock, or a milestone.</summary>
    public class PopupData
    {
        public string Name { get; set; } = "";          // the big line
        public string Icon { get; set; } = "";          // Steam icon address, or empty for the app icon
        public double? Percent { get; set; }            // rarity of an unlock (ignored if RightText is set)
        public string? Header { get; set; }             // small gold line, default "Achievement unlocked"
        public string? RightText { get; set; }          // small text on the right, instead of the rarity
        public bool Highlight { get; set; }             // show RightText in gold
        public string? CoverAppId { get; set; }         // show this game's cover art instead of an icon
    }
}
