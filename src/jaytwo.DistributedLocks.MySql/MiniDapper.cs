using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

internal static class MiniDapper
{
    public static async Task InitOpenConnectionAsync(this MySqlConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    public static async Task<object?> ExecuteScalarAsync(this MySqlConnection connection, string query, CancellationToken cancellationToken = default)
    {
        // not disposing the command because it will end the connection
        var command = new MySqlCommand(query, connection)
        {
        };

        await InitOpenConnectionAsync(connection, cancellationToken);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public static void InitOpenConnection(this IDbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
    }

    public static object ExecuteScalar(this MySqlConnection connection, string query)
    {
        // not disposing the command because it will end the connection
        var command = new MySqlCommand(query, connection)
        {
        };

        InitOpenConnection(connection);
        return command.ExecuteScalar();
    }

    public static bool IsPoolingEnabled(this MySqlConnection connection)
    {
        var connectionString = connection.ConnectionString;
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
        return connectionStringBuilder.Pooling;
    }

    public static async Task<T?> ExecuteScalarAsync<T>(this MySqlConnection connection, string query, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync(connection, query, cancellationToken);

        if (result is T value)
        {
            return value;
        }

        return default(T?);
    }
}
