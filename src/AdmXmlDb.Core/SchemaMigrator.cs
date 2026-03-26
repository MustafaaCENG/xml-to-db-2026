using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AdmXmlDb.Core;

/// <summary>
/// Migrates existing SQLite schema to add new columns and tables for legacy features.
/// </summary>
public static class SchemaMigrator
{
    public static void Migrate(AdmXmlDbContext db)
    {
        using var conn = new SqliteConnection(db.Database.GetConnectionString());
        conn.Open();

        EnsureTargetDirectoryComponentsTable(conn);
        AddIntegrationTaskColumns(conn);
        AddTaskMappingColumns(conn);
    }

    private static void EnsureTargetDirectoryComponentsTable(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='TargetDirectoryComponents'";
        if (cmd.ExecuteScalar() != null)
            return;

        using (var create = conn.CreateCommand())
        {
            create.CommandText = @"
                CREATE TABLE TargetDirectoryComponents (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TaskId INTEGER NOT NULL,
                    ComponentType INTEGER NOT NULL,
                    SortOrder INTEGER NOT NULL,
                    Value TEXT NOT NULL,
                    FOREIGN KEY (TaskId) REFERENCES Tasks(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_TargetDirectoryComponents_TaskId ON TargetDirectoryComponents(TaskId);";
            create.ExecuteNonQuery();
        }
    }

    private static void AddIntegrationTaskColumns(SqliteConnection conn)
    {
        var columns = new[] { "MultiRecordRootXPath", "TextToRemoveInXPath", "PrependToFileName", "StaticTargetDirectory", "FileNameSuffix", "AddDateTimeToFileName", "ProcessingDelaySeconds", "NetworkUsername", "EncryptedNetworkPassword" };
        foreach (var col in columns)
        {
            if (ColumnExists(conn, "Tasks", col))
                continue;
            using var cmd = conn.CreateCommand();
            var sqlType = col is "AddDateTimeToFileName" ? "INTEGER NOT NULL DEFAULT 0"
                       : col is "ProcessingDelaySeconds" ? "INTEGER NOT NULL DEFAULT 5"
                       : col is "EncryptedNetworkPassword" ? "BLOB"
                       : "TEXT";
            cmd.CommandText = $"ALTER TABLE Tasks ADD COLUMN {col} {sqlType}";
            try { cmd.ExecuteNonQuery(); } catch { /* column may exist */ }
        }
    }

    private static void AddTaskMappingColumns(SqliteConnection conn)
    {
        var columns = new[] { "DefaultValue", "FindValue", "ReplaceValue", "SortOrder", "IsLiteral", "ValueTemplate", "TargetTableName", "IsRequired" };
        foreach (var col in columns)
        {
            if (ColumnExists(conn, "TaskMappings", col))
                continue;
            using var cmd = conn.CreateCommand();
            var sqlType = col is "SortOrder" or "IsLiteral" or "IsRequired" ? "INTEGER NOT NULL DEFAULT 0" : "TEXT";
            cmd.CommandText = $"ALTER TABLE TaskMappings ADD COLUMN {col} {sqlType}";
            try { cmd.ExecuteNonQuery(); } catch { }
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
