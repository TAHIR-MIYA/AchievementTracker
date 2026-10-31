using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementTracker
{
    public class SaveInfo
    {
        public string Title { get; set; } = "";
        public List<string> Patterns { get; set; } = new List<string>();   // e.g. "<winAppData>/EldenRing/<storeUserId>"
        public DateTime CheckedUtc { get; set; }
    }

    /// <summary>
    /// Finds where a game keeps its save files, using the community Ludusavi manifest
    /// (github.com/mtkennerly/ludusavi-manifest), which maps Steam App IDs to save locations.
    /// The database (about 18 MB) is downloaded once and kept for 30 days.
    /// </summary>
    public static class SaveLocator
    {
        private const string ManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        private static readonly SemaphoreSlim ResolveLock = new SemaphoreSlim(1, 1);
        private static readonly object Gate = new object();
        private static Dictionary<string, SaveInfo> cache = LoadCache();

        static SaveLocator()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("AchievementTracker/1.0");
        }

        private static string DataDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AchievementTracker");
        private static string ManifestPath => Path.Combine(DataDir, "ludusavi_manifest.yaml");
        private static string CachePath => Path.Combine(DataDir, "save_locations.json");

        private static Dictionary<string, SaveInfo> LoadCache()
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    return JsonSerializer.Deserialize<Dictionary<string, SaveInfo>>(File.ReadAllText(CachePath))
                           ?? new Dictionary<string, SaveInfo>();
                }
            }
            catch { }
            return new Dictionary<string, SaveInfo>();
        }

        private static void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary>What we already know about a game (null = never looked up).</summary>
        public static SaveInfo? Get(string appId)
        {
            lock (Gate)
            {
                return cache.TryGetValue(appId, out SaveInfo? info) ? info : null;
            }
        }

        private static bool IsFresh(string appId)
        {
            SaveInfo? info = Get(appId);
            if (info == null) return false;
            return info.Patterns.Count > 0 || DateTime.UtcNow - info.CheckedUtc < TimeSpan.FromDays(30);
        }

        /// <summary>Looks the games up in the database. Downloads it first if needed. False if that failed.</summary>
        public static async Task<bool> ResolveAsync(IEnumerable<string> appIds)
        {
            await ResolveLock.WaitAsync().ConfigureAwait(false);
            try
            {
                List<string> missing = appIds.Where(id => id.Length > 0 && !IsFresh(id)).Distinct().ToList();
                if (missing.Count == 0) return true;

                await EnsureManifestAsync().ConfigureAwait(false);
                HashSet<string> wanted = new HashSet<string>(missing);
                Dictionary<string, SaveInfo> found = await Task.Run(() => ParseManifest(ManifestPath, wanted)).ConfigureAwait(false);

                lock (Gate)
                {
                    foreach (string id in missing)
                    {
                        // A game that isn't in the database is remembered as "no known location" for 30 days.
                        cache[id] = found.TryGetValue(id, out SaveInfo? info) ? info : new SaveInfo { CheckedUtc = DateTime.UtcNow };
                    }
                    SaveCache();
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                ResolveLock.Release();
            }
        }

        private static async Task EnsureManifestAsync()
        {
            FileInfo existing = new FileInfo(ManifestPath);
            bool fresh = existing.Exists && existing.Length > 1000000 && DateTime.UtcNow - existing.LastWriteTimeUtc < TimeSpan.FromDays(30);
            if (fresh) return;

            try
            {
                Directory.CreateDirectory(DataDir);
                string temp = ManifestPath + ".download";
                using (HttpResponseMessage response = await Http.GetAsync(ManifestUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (FileStream output = File.Create(temp))
                    {
                        await input.CopyToAsync(output).ConfigureAwait(false);
                    }
                }
                File.Move(temp, ManifestPath, true);
            }
            catch
            {
                // Offline? An older copy is still good enough. Only fail if there is none at all.
                if (!existing.Exists || existing.Length < 1000000) throw;
            }
        }

        // ------------------------------------------------------------------ reading the manifest

        private class ManifestFile
        {
            public string Path = "";
            public List<string> Tags = new List<string>();
            public List<string> Os = new List<string>();
        }

        // The manifest is a big, very regular YAML file, so it is read line by line (no YAML library needed).
        // Only entries tagged "save" that apply to Windows are kept, and only for the games we ask about.
        private static Dictionary<string, SaveInfo> ParseManifest(string path, HashSet<string> wanted)
        {
            Dictionary<string, SaveInfo> result = new Dictionary<string, SaveInfo>();
            string title = "";
            string section = "";
            string subKey = "";
            string steamId = "";
            bool inGame = false;
            List<ManifestFile> files = new List<ManifestFile>();

            foreach (string raw in File.ReadLines(path))
            {
                if (raw.Length == 0) continue;
                string text = raw.Trim();
                if (text.Length == 0 || text == "---") continue;
                int indent = raw.Length - raw.TrimStart(' ').Length;

                if (indent == 0 && text.EndsWith(":"))
                {
                    Commit(result, wanted, title, steamId, files);
                    title = Unquote(text.Substring(0, text.Length - 1));
                    steamId = "";
                    section = "";
                    subKey = "";
                    files = new List<ManifestFile>();
                    inGame = true;
                    continue;
                }
                if (!inGame) continue;

                if (indent == 2)
                {
                    section = text.TrimEnd(':').Split(':')[0];
                    subKey = "";
                    continue;
                }

                if (section == "steam")
                {
                    if (indent == 4 && text.StartsWith("id:")) steamId = text.Substring(3).Trim();
                    continue;
                }

                if (section == "files")
                {
                    if (indent == 4 && text.EndsWith(":"))
                    {
                        files.Add(new ManifestFile { Path = Unquote(text.Substring(0, text.Length - 1)) });
                        subKey = "";
                    }
                    else if (indent == 6)
                    {
                        subKey = text.TrimEnd(':');
                    }
                    else if (files.Count > 0 && subKey == "tags" && text.StartsWith("- "))
                    {
                        files[files.Count - 1].Tags.Add(text.Substring(2).Trim());
                    }
                    else if (files.Count > 0 && subKey == "when" && text.Contains("os:"))
                    {
                        files[files.Count - 1].Os.Add(text.Substring(text.IndexOf("os:", StringComparison.Ordinal) + 3).Trim());
                    }
                }
            }
            Commit(result, wanted, title, steamId, files);
            return result;
        }

        private static void Commit(Dictionary<string, SaveInfo> result, HashSet<string> wanted, string title, string steamId, List<ManifestFile> files)
        {
            if (steamId.Length == 0 || !wanted.Contains(steamId)) return;

            SaveInfo info = new SaveInfo { Title = title, CheckedUtc = DateTime.UtcNow };
            foreach (ManifestFile file in files)
            {
                bool isSave = file.Tags.Contains("save");
                bool forWindows = file.Os.Count == 0 || file.Os.Contains("windows");
                if (isSave && forWindows && !info.Patterns.Contains(file.Path)) info.Patterns.Add(file.Path);
            }
            result[steamId] = info;
        }

        private static string Unquote(string value)
        {
            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                return value.Substring(1, value.Length - 2).Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            return value;
        }

        // ------------------------------------------------------------------ turning patterns into real paths

        /// <summary>Every save file or folder that exists on this PC for the game (database locations plus folders you added).</summary>
        public static List<string> ExistingPaths(TrackedGame game)
        {
            List<string> result = new List<string>();
            SaveInfo? info = Get(game.AppId);
            if (info != null)
            {
                foreach (string pattern in info.Patterns) result.AddRange(Expand(pattern, game));
            }
            foreach (string manual in game.SavePaths)
            {
                if (Directory.Exists(manual) || File.Exists(manual)) result.Add(manual);
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> Expand(string pattern, TrackedGame game)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string p = pattern
                .Replace("<home>", userProfile)
                .Replace("<winAppData>", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
                .Replace("<winLocalAppData>", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
                .Replace("<winLocalAppDataLow>", Path.Combine(userProfile, "AppData", "LocalLow"))
                .Replace("<winDocuments>", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
                .Replace("<winPublic>", Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public")
                .Replace("<winProgramData>", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData))
                .Replace("<winDir>", Environment.GetFolderPath(Environment.SpecialFolder.Windows))
                .Replace("<osUserName>", Environment.UserName)
                .Replace("<storeUserId>", "*")          // the Steam user id folder: match any
                .Replace("<storeGameId>", game.AppId);

            if (p.Contains("<base>"))
            {
                if (string.IsNullOrWhiteSpace(game.InstallDir)) return new List<string>();   // we don't know where the game is installed
                p = p.Replace("<base>", game.InstallDir);
            }
            if (p.Contains("<")) return new List<string>();   // some other placeholder we can't resolve

            return Glob(p);
        }

        // Expands * and ? in a path, one folder level at a time. Returns only things that exist.
        private static List<string> Glob(string pattern)
        {
            string[] parts = pattern.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return new List<string>();

            List<string> current = new List<string> { parts[0] + "\\" };   // for example "C:\"
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                List<string> next = new List<string>();
                foreach (string basePath in current)
                {
                    if (part.IndexOfAny(new[] { '*', '?' }) < 0)
                    {
                        string candidate = Path.Combine(basePath, part);
                        if (File.Exists(candidate) || Directory.Exists(candidate)) next.Add(candidate);
                    }
                    else
                    {
                        try
                        {
                            foreach (string entry in Directory.EnumerateFileSystemEntries(basePath, part)) next.Add(entry);
                        }
                        catch { }
                    }
                }
                current = next;
                if (current.Count == 0) break;
            }
            return current.Where(path => File.Exists(path) || Directory.Exists(path)).ToList();
        }
    }
}
