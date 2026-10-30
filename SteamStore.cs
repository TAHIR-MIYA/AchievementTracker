using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AchievementTracker
{
    public enum LookupStatus { Found, NotFound, Offline }

    public class LookupResult
    {
        public LookupStatus Status { get; set; }
        public string Name { get; set; } = "";
        public string HeaderUrl { get; set; } = "";
    }

    /// <summary>Looks up a game's real name and cover art on the Steam store (no API key needed).</summary>
    public static class SteamStore
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        static SteamStore()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("AchievementTracker/1.0");
        }

        private static string CoverDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AchievementTracker", "covers");

        public static async Task<LookupResult> LookupAsync(string appId)
        {
            try
            {
                string url = "https://store.steampowered.com/api/appdetails?appids=" + Uri.EscapeDataString(appId) + "&l=english";
                using (HttpResponseMessage response = await Http.GetAsync(url).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return new LookupResult { Status = LookupStatus.Offline };

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty(appId, out JsonElement entry) &&
                            entry.TryGetProperty("success", out JsonElement ok) && ok.ValueKind == JsonValueKind.True &&
                            entry.TryGetProperty("data", out JsonElement data))
                        {
                            string name = data.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                            string header = data.TryGetProperty("header_image", out JsonElement h) ? h.GetString() ?? "" : "";
                            return new LookupResult { Status = LookupStatus.Found, Name = name, HeaderUrl = header };
                        }
                    }
                }
                return new LookupResult { Status = LookupStatus.NotFound };
            }
            catch
            {
                return new LookupResult { Status = LookupStatus.Offline };
            }
        }

        /// <summary>The game's cover image. Cached on disk, so it only downloads once. Null if unavailable.</summary>
        public static async Task<Image?> GetCoverAsync(string appId)
        {
            try
            {
                string file = Path.Combine(CoverDir, appId + ".jpg");
                if (File.Exists(file))
                {
                    Image? cached = FromBytes(File.ReadAllBytes(file));
                    if (cached != null) return cached;
                }

                Directory.CreateDirectory(CoverDir);

                // Fast path: the standard cover address. If that fails, ask the store for the exact address.
                string fallback = "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/header.jpg";
                Image? image = await DownloadCoverAsync(fallback, file).ConfigureAwait(false);
                if (image != null) return image;

                LookupResult info = await LookupAsync(appId).ConfigureAwait(false);
                if (info.Status == LookupStatus.Found && info.HeaderUrl.Length > 0)
                {
                    return await DownloadCoverAsync(info.HeaderUrl, file).ConfigureAwait(false);
                }
            }
            catch { }
            return null;
        }

        private static async Task<Image?> DownloadCoverAsync(string url, string saveTo)
        {
            try
            {
                byte[] bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                Image? image = FromBytes(bytes);
                if (image == null) return null;
                File.WriteAllBytes(saveTo, bytes);
                return image;
            }
            catch
            {
                return null;
            }
        }

        // Makes an independent copy, so the image doesn't depend on a stream staying open.
        private static Image? FromBytes(byte[] bytes)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms))
                {
                    return new Bitmap(tmp);
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
