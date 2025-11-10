using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public static class DbConnectionFactoryExtensions
{
    public static int ExecuteNonQuery(this IDbConnectionFactory factory, Action<DbCommand> builder)
    {
        using var connection = factory.OpenConnection();
        return connection.ExecuteNonQuery(builder);
    }

    public static async Task<int> ExecuteNonQueryAsync(this IDbConnectionFactory factory, Action<DbCommand> builder, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteNonQueryAsync(builder, cancellationToken);
    }

    public static T? ExecuteScalar<T>(this IDbConnectionFactory factory, Action<DbCommand> builder)
    {
        using var connection = factory.OpenConnection();
        return connection.ExecuteScalar<T>(builder);
    }

    public static async Task<T?> ExecuteScalarAsync<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<T>(builder, cancellationToken);
    }

    public static DbDataReader ExecuteReader(this IDbConnectionFactory factory, Action<DbCommand> builder)
        => factory.ExecuteReader(builder, CommandBehavior.Default);

    public static DbDataReader ExecuteReader(this IDbConnectionFactory factory, Action<DbCommand> builder, CommandBehavior commandBehavior)
    {
        var connection = factory.OpenConnection(); // not disposing here because the caller is expected to dispose the reader which will dispose the connection
        return connection.ExecuteReader(builder, commandBehavior | CommandBehavior.CloseConnection);
    }

    public static async Task<DbDataReader> ExecuteReaderAsync(this IDbConnectionFactory factory, Action<DbCommand> builder, CancellationToken cancellationToken = default)
        => await factory.ExecuteReaderAsync(builder, CommandBehavior.Default, cancellationToken);

    public static async Task<DbDataReader> ExecuteReaderAsync(this IDbConnectionFactory factory, Action<DbCommand> builder, CommandBehavior commandBehavior, CancellationToken cancellationToken = default)
    {
        var connection = await factory.OpenConnectionAsync(cancellationToken); // not disposing here because the caller is expected to dispose the reader which will dispose the connection
        return await connection.ExecuteReaderAsync(builder, commandBehavior | CommandBehavior.CloseConnection, cancellationToken);
    }
}
