namespace AdmXmlDb.Core;

public static class Constants
{
    /// <summary>
    /// Database path under %ProgramData%\AdmXmlDb\ so both the Management UI
    /// and the Windows Service (running as LocalSystem) share the same file.
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var dbDir = Path.Combine(programData, "AdmXmlDb");
        Directory.CreateDirectory(dbDir);
        var newPath = Path.Combine(dbDir, "admxmldb.db");

        MigrateFromOldLocationIfNeeded(newPath);

        return newPath;
    }

    /// <summary>
    /// Migration: finds the best old DB from any user's LocalAppData and copies it
    /// to the shared ProgramData location. Runs when the target doesn't exist or is
    /// empty (0 bytes / just created). Scans all user profiles so it works regardless
    /// of whether the service (LocalSystem) or the UI (interactive user) runs first.
    /// </summary>
    private static void MigrateFromOldLocationIfNeeded(string newPath)
    {
        try
        {
            bool targetUsable = File.Exists(newPath) && new FileInfo(newPath).Length > 0;
            if (targetUsable)
                return;

            var bestOldPath = FindBestOldDatabase();
            if (bestOldPath != null)
            {
                File.Copy(bestOldPath, newPath, overwrite: true);
            }
        }
        catch
        {
            // Migration is best-effort; if it fails the app will create a fresh DB
        }
    }

    private static string? FindBestOldDatabase()
    {
        var candidates = new List<string>();

        // 1) Current user's LocalAppData
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                candidates.Add(Path.Combine(localAppData, "AdmXmlDb", "admxmldb.db"));
        }
        catch { }

        // 2) Scan all user profiles (covers the case where service runs as LocalSystem
        //    but the DB was created by the interactive user)
        try
        {
            var usersDir = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            if (usersDir != null && Directory.Exists(usersDir))
            {
                foreach (var userDir in Directory.GetDirectories(usersDir))
                {
                    candidates.Add(Path.Combine(userDir, "AppData", "Local", "AdmXmlDb", "admxmldb.db"));
                }
            }
        }
        catch { }

        // Pick the largest existing file (most likely to have real task data)
        return candidates
            .Where(File.Exists)
            .Select(p => new FileInfo(p))
            .Where(fi => fi.Length > 0)
            .OrderByDescending(fi => fi.Length)
            .FirstOrDefault()?.FullName;
    }

    public const string ServiceName = "AdmXmlDbWorker";
}
