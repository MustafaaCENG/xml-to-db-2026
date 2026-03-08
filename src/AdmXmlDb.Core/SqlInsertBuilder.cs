using System.Text;
using Microsoft.Data.SqlClient;
using AdmXmlDb.Core.Entities;

namespace AdmXmlDb.Core;

/// <summary>
/// Builds parameterized SQL INSERT statements and executes them against SQL Server.
/// </summary>
public static class SqlInsertBuilder
{
    private static string EscapeSqlIdentifier(string name)
        => name.Replace("]", "]]");

    /// <summary>
    /// Builds a parameterized INSERT statement string (for display/simulation).
    /// </summary>
    public static string BuildInsertStatement(string tableName, Dictionary<string, object?> values)
    {
        if (values.Count == 0)
            return string.Empty;

        var escapedTable = EscapeSqlIdentifier(tableName);
        var columns = string.Join(", ", values.Keys.Select(c => $"[{EscapeSqlIdentifier(c)}]"));
        var paramsList = string.Join(", ", values.Keys.Select(c => $"@{c}"));
        return $"INSERT INTO [{escapedTable}] ({columns}) VALUES ({paramsList})";
    }

    /// <summary>
    /// Executes the INSERT using a parameterized command. Returns the number of rows affected.
    /// </summary>
    public static int ExecuteInsert(SqlConnection connection, SqlTransaction? transaction, string tableName, Dictionary<string, object?> values)
    {
        if (values.Count == 0)
            return 0;

        var sql = BuildInsertStatement(tableName, values);
        using var cmd = new SqlCommand(sql, connection, transaction);

        foreach (var (columnName, value) in values)
        {
            var param = cmd.Parameters.AddWithValue($"@{columnName}", value ?? DBNull.Value);
            if (value != null && value != DBNull.Value)
                param.SqlDbType = InferSqlDbType(value);
        }

        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Async version of ExecuteInsert for use in Worker service.
    /// </summary>
    public static async Task<int> ExecuteInsertAsync(SqlConnection connection, SqlTransaction? transaction, string tableName, Dictionary<string, object?> values, CancellationToken ct = default)
    {
        if (values.Count == 0)
            return 0;

        var sql = BuildInsertStatement(tableName, values);
        await using var cmd = new SqlCommand(sql, connection, transaction);

        foreach (var (columnName, value) in values)
        {
            var param = cmd.Parameters.AddWithValue($"@{columnName}", value ?? DBNull.Value);
            if (value != null && value != DBNull.Value)
                param.SqlDbType = InferSqlDbType(value);
        }

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static System.Data.SqlDbType InferSqlDbType(object value)
    {
        return value switch
        {
            int => System.Data.SqlDbType.Int,
            long => System.Data.SqlDbType.BigInt,
            bool => System.Data.SqlDbType.Bit,
            DateTime => System.Data.SqlDbType.DateTime,
            _ => System.Data.SqlDbType.NVarChar
        };
    }
}
