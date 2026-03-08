namespace AdmXmlDb.Core;

public static class Constants
{
    /// <summary>
    /// Default database path under %LocalAppData%\AdmXmlDb\
    /// </summary>
    public static string GetDefaultDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AdmXmlDb");
        Directory.CreateDirectory(dbDir);
        return Path.Combine(dbDir, "admxmldb.db");
    }

    public const string ServiceName = "AdmXmlDbWorker";
}
