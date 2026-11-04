using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AchievementTracker
{
    // One restorable item inside a backup zip (written to manifest.json)
    public class BackupItem
    {
        public string Kind { get; set; } = "";        // save, achievement, gamelist, history
        public string Game { get; set; } = "";
        public string AppId { get; set; } = "";
        public string EntryRoot { get; set; } = "";   // folder (or file) name inside the zip
        public string Original { get; set; } = "";    // where it came from on this PC
        public bool IsFile { get; set; }
    }

    public class BackupManifest
    {
        public int Version { get; set; } = 1;
        public string Created { get; set; } = "";
        public List<BackupItem> Items { get; set; } = new List<BackupItem>();
    }

    public class BackupResult
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Writes a zip with your progress into a folder you choose. If that folder is inside Google Drive,
    /// OneDrive or Dropbox, the cloud app uploads it by itself. The newest 10 backups are kept.
    /// </summary>
    public static class BackupService
    {
        private const int KeepBackups = 10;
        private const string Prefix = "AchievementTracker_backup_";
        private static readonly object Gate = new object();
        private static System.Threading.Timer? delayedRun;

        public static event EventHandler? Completed;
        public static string LastMessage { get; private set; } = "";
        public static bool LastOk { get; private set; }

        /// <summary>Called after an unlock. Waits 30 seconds (and at least 10 minutes since the last backup), so a burst of unlocks makes just one backup.</summary>
        public static void ScheduleAfterUnlock()
        {
            AppSettings settings = AppSettings.Current;
            if (!settings.AutoBackup || string.IsNullOrWhiteSpace(settings.BackupFolder)) return;

            // 30 seconds after the unlock, but never more often than once every 10 minutes
            double untilAllowed = (TimeSpan.FromMinutes(10) - (DateTime.UtcNow - settings.LastBackupUtc)).TotalMilliseconds;
            int delay = (int)Math.Max(30000, untilAllowed);

            lock (Gate)
            {
                delayedRun?.Dispose();
                delayedRun = new System.Threading.Timer(_ => RunNow(Program.GetTrackedGamesSnapshot()), null, delay, System.Threading.Timeout.Infinite);
            }
        }

        /// <summary>Called once at startup: makes a backup if automatic backup is on and the last one is over a day old.</summary>
        public static void StartupCheck()
        {
            Task.Run(async () =>
            {
                await Task.Delay(15000);
                AppSettings settings = AppSettings.Current;
                if (!settings.AutoBackup || string.IsNullOrWhiteSpace(settings.BackupFolder)) return;
                if (DateTime.UtcNow - settings.LastBackupUtc < TimeSpan.FromHours(24)) return;
                RunNow(Program.GetTrackedGamesSnapshot());
            });
        }

        public static BackupResult RunNow(List<TrackedGame> games)
        {
            BackupResult result;
            lock (Gate)
            {
                result = Create(games);
                LastMessage = result.Message;
                LastOk = result.Ok;
            }
            Completed?.Invoke(null, EventArgs.Empty);
            return result;
        }

        private static BackupResult Create(List<TrackedGame> games)
        {
            AppSettings settings = AppSettings.Current;
            if (string.IsNullOrWhiteSpace(settings.BackupFolder))
                return new BackupResult { Ok = false, Message = "Choose a backup folder first." };

            string zipPath = "";
            try
            {
                Directory.CreateDirectory(settings.BackupFolder);
                zipPath = Path.Combine(settings.BackupFolder, Prefix + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
                int fileCount = 0;
                int saveFileCount = 0;
                List<string> sources = new List<string>();
                List<BackupItem> items = new List<BackupItem>();

                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    int gameListAdded = AddFile(zip, AppPaths.InBase("tracked_games.json"), "tracker/tracked_games.json");
                    if (gameListAdded > 0)
                        items.Add(new BackupItem { Kind = "gamelist", EntryRoot = "tracker/tracked_games.json", Original = AppPaths.InBase("tracked_games.json"), IsFile = true });
                    int historyAdded = AddFile(zip, UnlockHistory.FilePath, "tracker/unlock_history.json");
                    if (historyAdded > 0)
                        items.Add(new BackupItem { Kind = "history", EntryRoot = "tracker/unlock_history.json", Original = UnlockHistory.FilePath, IsFile = true });
                    fileCount += gameListAdded + historyAdded;

                    // Settings, but never the Steam API key
                    AppSettings? copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
                    if (copy != null)
                    {
                        copy.SteamApiKey = "";
                        AddText(zip, "tracker/settings.json", JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
                        fileCount++;
                    }

                    // The achievement files of every tracked game (Goldberg, CODEX, RUNE, FLT, TENOKE, OnlineFix)
                    foreach (TrackedGame game in games)
                    {
                        if (!uint.TryParse(game.AppId, out _)) continue;
                        foreach (var folder in Program.GetAchievementFolders(game.AppId))
                        {
                            int added = 0;
                            foreach (string file in SafeFiles(folder.Dir))
                            {
                                string relative = Path.GetRelativePath(folder.Dir, file).Replace('\\', '/');
                                added += AddFile(zip, file, "achievements/" + folder.Label + "/" + game.AppId + "/" + relative);
                            }
                            if (added > 0)
                            {
                                fileCount += added;
                                sources.Add("achievements/" + folder.Label + "/" + game.AppId + "/   came from   " + folder.Dir);
                                items.Add(new BackupItem
                                {
                                    Kind = "achievement", Game = game.Name, AppId = game.AppId,
                                    EntryRoot = "achievements/" + folder.Label + "/" + game.AppId + "/", Original = folder.Dir, IsFile = false
                                });
                            }
                        }
                    }

                    // The games' own save files (found through the community database, plus folders you added per game)
                    if (settings.IncludeGameSaves)
                    {
                        try { SaveLocator.ResolveAsync(games.Select(game => game.AppId)).GetAwaiter().GetResult(); } catch { }

                        foreach (TrackedGame game in games)
                        {
                            long gameBytes = 0;
                            int locationNumber = 0;
                            foreach (string location in SaveLocator.ExistingPaths(game))
                            {
                                locationNumber++;
                                string entryRoot = "saves/" + SafeEntry(game.Name) + " (" + game.AppId + ")/" + locationNumber + "/";
                                int added = AddLocation(zip, location, entryRoot, ref gameBytes);
                                if (added > 0)
                                {
                                    fileCount += added;
                                    saveFileCount += added;
                                    sources.Add(entryRoot + "   came from   " + location);
                                    items.Add(new BackupItem
                                    {
                                        Kind = "save", Game = game.Name, AppId = game.AppId,
                                        EntryRoot = entryRoot, Original = location, IsFile = File.Exists(location)
                                    });
                                }
                            }
                        }
                    }

                    AddText(zip, "manifest.json", JsonSerializer.Serialize(
                        new BackupManifest { Created = DateTime.Now.ToString("s"), Items = items },
                        new JsonSerializerOptions { WriteIndented = true }));
                    AddText(zip, "README.txt", BuildReadme(fileCount, sources));
                }

                Prune(settings.BackupFolder);
                settings.LastBackupUtc = DateTime.UtcNow;
                AppSettings.Save();
                double megabytes = new FileInfo(zipPath).Length / (1024.0 * 1024.0);
                return new BackupResult
                {
                    Ok = true,
                    Message = "Backed up " + fileCount + " files (" + saveFileCount + " game saves, " + megabytes.ToString("0.0") + " MB) at " +
                              DateTime.Now.ToString("HH:mm") + " in " + Path.GetFileName(zipPath)
                };
            }
            catch (Exception ex)
            {
                try { if (zipPath.Length > 0 && File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                return new BackupResult { Ok = false, Message = "Backup failed: " + ex.Message };
            }
        }

        // Reads with FileShare.ReadWrite, so a file the emulator is writing right now doesn't block the backup.
        private static int AddFile(ZipArchive zip, string sourcePath, string entryName, CompressionLevel level = CompressionLevel.Optimal)
        {
            try
            {
                if (!File.Exists(sourcePath)) return 0;
                ZipArchiveEntry entry = zip.CreateEntry(entryName, level);
                using (FileStream input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (Stream output = entry.Open())
                {
                    input.CopyTo(output);
                }
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private const long MaxFileBytes = 200L * 1024 * 1024;       // skip single files bigger than this
        private const long MaxBytesPerGame = 1024L * 1024 * 1024;   // and stop adding to a game after 1 GB

        // Adds one save file, or a whole save folder, under entryRoot. Returns how many files were added.
        private static int AddLocation(ZipArchive zip, string location, string entryRoot, ref long gameBytes)
        {
            int count = 0;
            if (File.Exists(location))
            {
                return AddSaveFile(zip, location, entryRoot + Path.GetFileName(location), ref gameBytes);
            }
            if (Directory.Exists(location))
            {
                foreach (string file in SafeSaveFiles(location))
                {
                    string relative = Path.GetRelativePath(location, file).Replace('\\', '/');
                    count += AddSaveFile(zip, file, entryRoot + relative, ref gameBytes);
                }
            }
            return count;
        }

        private static int AddSaveFile(ZipArchive zip, string file, string entryName, ref long gameBytes)
        {
            try
            {
                long length = new FileInfo(file).Length;
                if (length > MaxFileBytes || gameBytes + length > MaxBytesPerGame) return 0;
                int added = AddFile(zip, file, entryName, CompressionLevel.Fastest);
                if (added > 0) gameBytes += length;
                return added;
            }
            catch
            {
                return 0;
            }
        }

        private static List<string> SafeSaveFiles(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Take(5000).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string SafeEntry(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            return name.Length == 0 ? "game" : name;
        }

        private static void AddText(ZipArchive zip, string entryName, string text)
        {
            ZipArchiveEntry entry = zip.CreateEntry(entryName);
            using (StreamWriter writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(text);
            }
        }

        private static List<string> SafeFiles(string folder)
        {
            try
            {
                return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Where(f => new FileInfo(f).Length < 5000000)
                    .Take(300)
                    .ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static void Prune(string folder)
        {
            try
            {
                foreach (string old in Directory.GetFiles(folder, Prefix + "*.zip").OrderByDescending(f => f).Skip(KeepBackups))
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }
        }

        private static string BuildReadme(int fileCount, List<string> sources)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("Achievement Tracker backup");
            text.AppendLine("Made: " + DateTime.Now.ToString("d MMM yyyy, HH:mm"));
            text.AppendLine("Files: " + fileCount);
            text.AppendLine();
            text.AppendLine("Your Steam API key is NOT included.");
            text.AppendLine();
            text.AppendLine("HOW TO RESTORE");
            text.AppendLine("1. Close Achievement Tracker (Exit from the tray menu).");
            text.AppendLine("2. Extract this zip somewhere.");
            text.AppendLine("3. Copy the CONTENTS of each folder inside \"saves\" and \"achievements\" back to the place named below.");
            text.AppendLine("4. Copy tracked_games.json next to the .exe, and unlock_history.json to");
            text.AppendLine("   %APPDATA%\\AchievementTracker\\ (settings.json goes there too, if you want it).");
            text.AppendLine();
            text.AppendLine("GAME SAVES AND ACHIEVEMENT FILES");
            if (sources.Count == 0) text.AppendLine("(none were found)");
            foreach (string line in sources) text.AppendLine(line);
            return text.ToString();
        }
    }
}
