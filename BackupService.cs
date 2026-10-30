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

        /// <summary>Called after an unlock. Waits 30 seconds, so a burst of unlocks makes just one backup.</summary>
        public static void ScheduleAfterUnlock()
        {
            AppSettings settings = AppSettings.Current;
            if (!settings.AutoBackup || string.IsNullOrWhiteSpace(settings.BackupFolder)) return;

            lock (Gate)
            {
                delayedRun?.Dispose();
                delayedRun = new System.Threading.Timer(_ => RunNow(Program.GetTrackedGamesSnapshot()), null, 30000, System.Threading.Timeout.Infinite);
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
                List<string> sources = new List<string>();

                using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    fileCount += AddFile(zip, AppPaths.InBase("tracked_games.json"), "tracker/tracked_games.json");
                    fileCount += AddFile(zip, UnlockHistory.FilePath, "tracker/unlock_history.json");

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
                            }
                        }
                    }

                    AddText(zip, "README.txt", BuildReadme(fileCount, sources));
                }

                Prune(settings.BackupFolder);
                settings.LastBackupUtc = DateTime.UtcNow;
                AppSettings.Save();
                return new BackupResult
                {
                    Ok = true,
                    Message = "Backed up " + fileCount + " files at " + DateTime.Now.ToString("HH:mm") + " (" + Path.GetFileName(zipPath) + ")"
                };
            }
            catch (Exception ex)
            {
                try { if (zipPath.Length > 0 && File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                return new BackupResult { Ok = false, Message = "Backup failed: " + ex.Message };
            }
        }

        // Reads with FileShare.ReadWrite, so a file the emulator is writing right now doesn't block the backup.
        private static int AddFile(ZipArchive zip, string sourcePath, string entryName)
        {
            try
            {
                if (!File.Exists(sourcePath)) return 0;
                ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
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
            text.AppendLine("3. Copy each folder inside \"achievements\" back to the place named below.");
            text.AppendLine("4. Copy tracked_games.json next to the .exe, and unlock_history.json to");
            text.AppendLine("   %APPDATA%\\AchievementTracker\\ (settings.json goes there too, if you want it).");
            text.AppendLine();
            text.AppendLine("ACHIEVEMENT FILES");
            if (sources.Count == 0) text.AppendLine("(none were found)");
            foreach (string line in sources) text.AppendLine(line);
            return text.ToString();
        }
    }
}
