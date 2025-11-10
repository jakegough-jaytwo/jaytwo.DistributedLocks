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
        => ExecuteReader(factory, builder, CommandBehavior.Default);

    public static DbDataReader ExecuteReader(this IDbConnectionFactory factory, Action<DbCommand> builder, CommandBehavior behavior)
    {
        var connection = factory.OpenConnection(); // not disposing here because the caller is expected to dispose the reader which will dispose the connection
        return connection.ExecuteReader(builder, behavior | CommandBehavior.CloseConnection);
    }

    public static async Task<DbDataReader> ExecuteReaderAsync(this IDbConnectionFactory factory, Action<DbCommand> builder, CancellationToken cancellationToken = default)
        => await ExecuteReaderAsync(factory, builder, CommandBehavior.Default, cancellationToken);

    public static async Task<DbDataReader> ExecuteReaderAsync(this IDbConnectionFactory factory, Action<DbCommand> builder, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        var connection = await factory.OpenConnectionAsync(cancellationToken); // not disposing here because the caller is expected to dispose the reader which will dispose the connection
        return await connection.ExecuteReaderAsync(builder, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
    }

    public static T QueryFirst<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        using var connection = factory.OpenConnection();
        return connection.QueryFirst<T>(builder, map, behavior | CommandBehavior.CloseConnection);
    }

    public static async Task<T> QueryFirstAsync<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QueryFirstAsync<T>(builder, map, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
    }

    public static T? QueryFirstOrDefault<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        using var connection = factory.OpenConnection();
        return connection.QueryFirst<T>(builder, map, behavior | CommandBehavior.CloseConnection);
    }

    public static async Task<T?> QueryFirstOrDefaultAsync<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QueryFirstOrDefaultAsync<T>(builder, map, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
    }

    public static T QuerySingle<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        using var connection = factory.OpenConnection();
        return connection.QuerySingle<T>(builder, map, behavior | CommandBehavior.CloseConnection);
    }

    public static async Task<T> QuerySingleAsync<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync<T>(builder, map, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
    }

    public static T? QuerySingleOrDefault<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        using var connection = factory.OpenConnection();
        return connection.QuerySingle<T>(builder, map, behavior | CommandBehavior.CloseConnection);
    }

    public static async Task<T?> QuerySingleOrDefaultAsync<T>(this IDbConnectionFactory factory, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleOrDefaultAsync<T>(builder, map, behavior | CommandBehavior.CloseConnection, cancellationToken).ConfigureAwait(false);
    }
}
