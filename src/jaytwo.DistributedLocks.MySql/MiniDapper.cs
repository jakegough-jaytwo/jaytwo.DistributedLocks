using System.Data;
using System.Data.Common;

namespace jaytwo.DistributedLocks.MySql;

internal static class MiniDapper
{
    public static async Task InitOpenConnectionAsync(this DbConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    public static DbCommand CreateCommand(this DbConnection connection, string query, int? timeoutSeconds = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = timeoutSeconds ?? command.CommandTimeout;

        return command;
    }

    public static async Task<int> ExecuteNonQueryAsync(this DbConnection connection, string query, int? timeoutSeconds = default, CancellationToken cancellationToken = default)
    {
        using var command = connection.CreateCommand(query, timeoutSeconds);
        await InitOpenConnectionAsync(connection, cancellationToken);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<object?> ExecuteScalarAsync(this DbConnection connection, string query, int? timeoutSeconds = default, CancellationToken cancellationToken = default)
    {
        using var command = connection.CreateCommand(query, timeoutSeconds);
        await InitOpenConnectionAsync(connection, cancellationToken);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static async Task<T?> ExecuteScalarAsync<T>(this DbConnection connection, string query, int? timeoutSeconds = default, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync(connection, query, timeoutSeconds, cancellationToken);

        // TODO: better type conversion
        if (result == null || result == DBNull.Value)
        {
            return default;
        }
        else if (result is T value)
        {
            return value;
        }

        var converted = (T?)Convert.ChangeType(result, typeof(T?));
        return converted;
    }
}
