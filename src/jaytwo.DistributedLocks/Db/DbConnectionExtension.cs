using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public static class DbConnectionExtension
{
    public static int ExecuteNonQuery(this DbConnection connection, Action<DbCommand> builder)
    {
        using var command = connection.CreateCommand();
        builder(command);
        return command.ExecuteNonQuery();
    }

    public static async Task<int> ExecuteNonQueryAsync(this DbConnection connection, Action<DbCommand> builder, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        builder(command);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static T? ExecuteScalar<T>(this DbConnection connection, Action<DbCommand> builder)
    {
        using var command = connection.CreateCommand();
        builder(command);
        return command.ExecuteScalar<T>();
    }

    public static async Task<T?> ExecuteScalarAsync<T>(this DbConnection connection, Action<DbCommand> builder, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        builder(command);
        return await command.ExecuteScalarAsync<T>(cancellationToken);
    }

    public static DbDataReader ExecuteReader(this DbConnection connection, Action<DbCommand> builder, CommandBehavior commandBehavior)
    {
        var command = connection.CreateCommand(); // not disposing here because the caller is expected to dispose the reader which will dispose the command
        builder(command);
        return command.ExecuteReader(commandBehavior);
    }

    public static DbDataReader ExecuteReader(this DbConnection connection, Action<DbCommand> builder)
        => ExecuteReader(connection, builder, CommandBehavior.Default);

    public static async Task<DbDataReader> ExecuteReaderAsync(this DbConnection connection, Action<DbCommand> builder, CommandBehavior commandBehavior, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand(); // not disposing here because the caller is expected to dispose the reader which will dispose the command
        builder(command);
        return await command.ExecuteReaderAsync(commandBehavior, cancellationToken);
    }

    public static async Task<DbDataReader> ExecuteReaderAsync(this DbConnection connection, Action<DbCommand> builder, CancellationToken cancellationToken = default)
        => await ExecuteReaderAsync(connection, builder, CommandBehavior.Default, cancellationToken);
}
