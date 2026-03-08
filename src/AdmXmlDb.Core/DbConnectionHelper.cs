using Microsoft.Data.SqlClient;

namespace AdmXmlDb.Core;

/// <summary>
/// Helper for database connection and schema operations.
/// </summary>
public static class DbConnectionHelper
{
    /// <summary>
    /// Builds a SQL Server connection string from components.
    /// </summary>
    public static string BuildConnectionString(string server, string database, string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(server))
            return "";

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server.Trim(),
            InitialCatalog = string.IsNullOrWhiteSpace(database) ? "master" : database.Trim(),
            TrustServerCertificate = true
        };

        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
        {
            builder.UserID = username.Trim();
            builder.Password = password;
            builder.IntegratedSecurity = false;
        }
        else
        {
            builder.IntegratedSecurity = true;
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Parses a connection string into components (password is never returned).
    /// </summary>
    public static (string Server, string Database, string? Username) ParseConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return ("", "", null);

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            return (
                builder.DataSource ?? "",
                builder.InitialCatalog ?? "",
                builder.IntegratedSecurity ? null : builder.UserID
            );
        }
        catch
        {
            return ("", "", null);
        }
    }

    /// <summary>
    /// Tests the connection and returns success message or error.
    /// </summary>
    public static (bool Success, string Message) TestConnection(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return (false, "Connection string is empty.");

        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();
            return (true, "Connection successful!");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Fetches column names and types from the specified table.
    /// Table name can include schema, e.g. ext.T_SO_DocumentExport.
    /// </summary>
    public static List<(string ColumnName, string DataType)> GetTableColumns(string connectionString, string tableName)
    {
        if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(tableName))
            return [];

        var parts = tableName.Trim().Split('.');
        var schema = parts.Length > 1 ? parts[0].Trim() : "dbo";
        var table = parts.Length > 1 ? parts[1].Trim() : parts[0].Trim();

        var result = new List<(string, string)>();

        try
        {
            using var conn = new SqlConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COLUMN_NAME, DATA_TYPE
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY ORDINAL_POSITION";
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", table);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add((r.GetString(0), r.GetString(1)));
            }
        }
        catch
        {
            // Return empty on error
        }

        return result;
    }
}
