using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AchievementTracker
{
    public class BackupFileInfo
    {
        public string Path { get; set; } = "";
        public DateTime Time { get; set; }
        public long Size { get; set; }
    }

    /// <summary>One thing that can be restored from a backup (a game's saves, its achievement files, the game list...).</summary>
    public class RestoreGroup
    {
        public BackupItem Item { get; set; } = new BackupItem();
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";       // where it will be written
        public string Status { get; set; } = "";       // "replaces 3 files", "new"
        public bool Checked { get; set; } = true;
        public bool Allowed { get; set; } = true;      // false if the target is outside the places we allow
        public List<(string EntryName, string Target)> Files { get; set; } = new List<(string EntryName, string Target)>();
    }

    public class RestoreResult
    {
        public int Restored { get; set; }
        public int Failed { get; set; }
        public string SafetyCopy { get; set; } = "";
        public string? GameListJson { get; set; }
        public string? HistoryJson { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
    }

    public static class RestoreService
    {
        private static string SafetyFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AchievementTracker", "restore_safety");

        public static List<BackupFileInfo> ListBackups(string folder)
        {
            List<BackupFileInfo> list = new List<BackupFileInfo>();
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return list;
                foreach (string file in Directory.GetFiles(folder, "AchievementTracker_backup_*.zip"))
                {
                    FileInfo info = new FileInfo(file);
                    list.Add(new BackupFileInfo { Path = file, Time = info.LastWriteTime, Size = info.Length });
                }
            }
            catch { }
            return list.OrderByDescending(b => b.Time).ToList();
        }

        /// <summary>Reads a backup and works out what it would restore. Null plus an error message if it can't be used.</summary>
        public static List<RestoreGroup>? LoadPlan(string zipPath, List<TrackedGame> games, out string error)
        {
            error = "";
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    ZipArchiveEntry? manifestEntry = zip.GetEntry("manifest.json");
                    if (manifestEntry == null)
                    {
                        error = "This backup was made by an older version and has no restore map. Open README.txt inside the zip to restore it by hand.";
                        return null;
                    }

                    BackupManifest? manifest;
                    using (StreamReader reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8))
                    {
                        manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd());
                    }
                    if (manifest == null || manifest.Items.Count == 0)
                    {
                        error = "This backup doesn't contain anything that can be restored.";
                        return null;
                    }

                    List<RestoreGroup> groups = new List<RestoreGroup>();
                    foreach (BackupItem item in manifest.Items)
                    {
                        RestoreGroup group = BuildGroup(zip, item, games);
                        if (group.Files.Count > 0) groups.Add(group);
                    }
                    if (groups.Count == 0) error = "Nothing in this backup could be matched to a place on this PC.";
                    return groups.Count > 0 ? groups : null;
                }
            }
            catch (Exception ex)
            {
                error = "Couldn't read this backup: " + ex.Message;
                return null;
            }
        }

        private static RestoreGroup BuildGroup(ZipArchive zip, BackupItem item, List<TrackedGame> games)
        {
            RestoreGroup group = new RestoreGroup { Item = item };

            switch (item.Kind)
            {
                case "save": group.Title = item.Game + ": save files"; break;
                case "achievement": group.Title = item.Game + ": achievement files"; break;
                case "gamelist": group.Title = "Your game list"; break;
                case "history": group.Title = "Your unlock history"; break;
                default: group.Title = item.Kind; break;
            }
            group.Detail = item.Original;

            string originalFull;
            try { originalFull = Path.GetFullPath(item.Original); }
            catch { group.Allowed = false; group.Checked = false; group.Status = "invalid location"; return group; }

            // Never write outside the places where saves, achievements and the app's own data belong.
            if (!IsAllowedTarget(originalFull, item.Kind, games))
            {
                group.Allowed = false;
                group.Checked = false;
                group.Status = "location not allowed";
                return group;
            }

            if (item.IsFile)
            {
                ZipArchiveEntry? entry = zip.GetEntry(item.EntryRoot) ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith(item.EntryRoot, StringComparison.Ordinal) && e.Name.Length > 0);
                if (entry != null) group.Files.Add((entry.FullName, originalFull));
            }
            else
            {
                string rootPrefix = originalFull.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.Name.Length == 0 || !entry.FullName.StartsWith(item.EntryRoot, StringComparison.Ordinal)) continue;
                    string relative = entry.FullName.Substring(item.EntryRoot.Length).Replace('/', Path.DirectorySeparatorChar);

                    string target;
                    try { target = Path.GetFullPath(Path.Combine(originalFull, relative)); }
                    catch { continue; }
                    // zip-slip protection: the file must end up inside the original folder
                    if (!target.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                    group.Files.Add((entry.FullName, target));
                }
            }

            int replaces = group.Files.Count(f => File.Exists(f.Target));
            group.Status = replaces == 0
                ? group.Files.Count + (group.Files.Count == 1 ? " file" : " files")
                : "replaces " + replaces + (replaces == 1 ? " file" : " files");
            return group;
        }

        private static bool IsAllowedTarget(string fullPath, string kind, List<TrackedGame> games)
        {
            if (kind == "gamelist") return string.Equals(fullPath, Path.GetFullPath(AppPaths.InBase("tracked_games.json")), StringComparison.OrdinalIgnoreCase);
            if (kind == "history") return string.Equals(fullPath, Path.GetFullPath(UnlockHistory.FilePath), StringComparison.OrdinalIgnoreCase);

            List<string> roots = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public",
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            };
            foreach (TrackedGame game in games)
            {
                if (!string.IsNullOrWhiteSpace(game.InstallDir)) roots.Add(game.InstallDir);
                roots.AddRange(game.SavePaths);
            }

            foreach (string root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                try
                {
                    string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                    if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Restores the chosen groups. Anything that would be replaced is copied into a safety zip first.
        /// </summary>
        public static RestoreResult Apply(string zipPath, List<RestoreGroup> chosen)
        {
            RestoreResult result = new RestoreResult();
            Program.SuppressAlertsFor(20);   // restored achievement files must not pop up "unlocked" for every old unlock

            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    result.SafetyCopy = MakeSafetyCopy(chosen);

                    foreach (RestoreGroup group in chosen)
                    {
                        foreach (var file in group.Files)
                        {
                            try
                            {
                                ZipArchiveEntry? entry = zip.GetEntry(file.EntryName);
                                if (entry == null) { result.Failed++; continue; }

                                if (group.Item.Kind == "gamelist" || group.Item.Kind == "history")
                                {
                                    // These two are applied by the app itself (they live in memory while it runs).
                                    using (StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8))
                                    {
                                        string json = reader.ReadToEnd();
                                        if (group.Item.Kind == "gamelist") result.GameListJson = json; else result.HistoryJson = json;
                                    }
                                    result.Restored++;
                                    continue;
                                }

                                Directory.CreateDirectory(Path.GetDirectoryName(file.Target)!);
                                entry.ExtractToFile(file.Target, true);
                                result.Restored++;
                            }
                            catch (Exception ex)
                            {
                                result.Failed++;
                                if (result.Problems.Count < 5) result.Problems.Add(Path.GetFileName(file.Target) + ": " + ex.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Problems.Add(ex.Message);
            }

            Program.SuppressAlertsFor(15);
            return result;
        }

        // Copies every file that is about to be replaced into a zip, so a restore can always be undone by hand.
        private static string MakeSafetyCopy(List<RestoreGroup> chosen)
        {
            List<string> existing = chosen
                .Where(g => g.Item.Kind != "gamelist" && g.Item.Kind != "history")
                .SelectMany(g => g.Files.Select(f => f.Target))
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // The game list and history are replaced in memory too, so keep their current files as well.
            foreach (RestoreGroup special in chosen.Where(grp => grp.Item.Kind == "gamelist" || grp.Item.Kind == "history"))
            {
                if (File.Exists(special.Item.Original)) existing.Add(special.Item.Original);
            }
            if (existing.Count == 0) return "";

            Directory.CreateDirectory(SafetyFolder);
            string safetyPath = Path.Combine(SafetyFolder, "before_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
            using (ZipArchive safety = ZipFile.Open(safetyPath, ZipArchiveMode.Create))
            {
                foreach (string file in existing)
                {
                    try
                    {
                        string entryName = "files/" + file.Replace(":", "").Replace('\\', '/').TrimStart('/');
                        ZipArchiveEntry entry = safety.CreateEntry(entryName, CompressionLevel.Fastest);
                        using (FileStream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (Stream output = entry.Open())
                        {
                            input.CopyTo(output);
                        }
                    }
                    catch { }
                }
                ZipArchiveEntry note = safety.CreateEntry("README.txt");
                using (StreamWriter writer = new StreamWriter(note.Open(), new UTF8Encoding(false)))
                {
                    writer.WriteLine("Safety copy made by Achievement Tracker before a restore on " + DateTime.Now.ToString("d MMM yyyy, HH:mm") + ".");
                    writer.WriteLine("It holds the files that the restore replaced. Inside \"files\", the folders follow the original paths (the drive letter is the first folder).");
                    writer.WriteLine("To undo the restore, copy them back to where they came from.");
                }
            }
            return safetyPath;
        }
    }
}
