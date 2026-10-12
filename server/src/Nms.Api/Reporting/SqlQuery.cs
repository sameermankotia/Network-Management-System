using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Data;

namespace Nms.Api.Reporting;

/// Analytics run as hand-written SQL: bucketing, window functions and filtered
/// aggregates are the whole point of these queries, and LINQ only gets in the way.
public class SqlQuery(NmsDbContext db)
{
    public async Task<List<T>> ListAsync<T>(
        string sql,
        Func<DbDataReader, T> map,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(ct);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters ?? new Dictionary<string, object?>())
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }

    public async Task<T?> SingleAsync<T>(
        string sql,
        Func<DbDataReader, T> map,
        IReadOnlyDictionary<string, object?>? parameters = null,
        CancellationToken ct = default)
    {
        var rows = await ListAsync(sql, map, parameters, ct);
        return rows.Count == 0 ? default : rows[0];
    }
}

public static class ReaderExtensions
{
    public static double? Double(this DbDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? null : Convert.ToDouble(reader.GetValue(index));
    }

    public static double DoubleOrZero(this DbDataReader reader, string column) => reader.Double(column) ?? 0;

    public static long LongOrZero(this DbDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? 0 : Convert.ToInt64(reader.GetValue(index));
    }

    public static int IntOrZero(this DbDataReader reader, string column) => (int)reader.LongOrZero(column);

    public static Guid Guid(this DbDataReader reader, string column) => reader.GetGuid(reader.GetOrdinal(column));

    public static string Text(this DbDataReader reader, string column)
    {
        var index = reader.GetOrdinal(column);
        return reader.IsDBNull(index) ? "" : reader.GetString(index);
    }

    public static DateTimeOffset Timestamp(this DbDataReader reader, string column)
    {
        var value = reader.GetValue(reader.GetOrdinal(column));
        return value switch
        {
            DateTimeOffset offset => offset,
            DateTime dateTime => new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero),
            _ => throw new InvalidCastException($"Column {column} is not a timestamp")
        };
    }
}
