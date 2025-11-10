using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public static class DbConnectionExtensions
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
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        return await command.ExecuteScalarAsync<T>(cancellationToken).ConfigureAwait(false);
    }

    public static DbDataReader ExecuteReader(this DbConnection connection, Action<DbCommand> builder, CommandBehavior behavior)
    {
        var command = connection.CreateCommand(); // not disposing here because the caller is expected to dispose the reader which will dispose the command
        try
        {
            builder(command);
            var inner = command.ExecuteReader(behavior);
            return new WrappedDbDataReader(inner, command);
        }
        catch
        {
            command.Dispose();
            throw;
        }
    }

    public static DbDataReader ExecuteReader(this DbConnection connection, Action<DbCommand> builder)
        => ExecuteReader(connection, builder, CommandBehavior.Default);

    public static async Task<DbDataReader> ExecuteReaderAsync(this DbConnection connection, Action<DbCommand> builder, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand(); // not disposing here because the caller is expected to dispose the reader which will dispose the command
        try
        {
            builder(command);
            var inner = await command.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
            return new WrappedDbDataReader(inner, command);
        }
        catch
        {
            await command.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<DbDataReader> ExecuteReaderAsync(this DbConnection connection, Action<DbCommand> builder, CancellationToken cancellationToken = default)
        => await ExecuteReaderAsync(connection, builder, CommandBehavior.Default, cancellationToken);

    public static async IAsyncEnumerable<T> ExecuteReaderAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, int, T> map, CommandBehavior behavior, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var reader = await connection.ExecuteReaderAsync(builder, behavior, cancellationToken).ConfigureAwait(false);

        int index = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return map(reader, index);
            index++;
        }
    }

    public static async IAsyncEnumerable<T> ExecuteReaderAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var iterator = ExecuteReaderAsync(connection, builder, (reader, index) => map(reader), behavior, cancellationToken);

        await foreach (var item in iterator.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public static IEnumerable<T> ExecuteReader<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, int, T> map, CommandBehavior behavior)
    {
        using var reader = connection.ExecuteReader(builder, behavior);

        int index = 0;
        while (reader.Read())
        {
            yield return map(reader, index);
            index++;
        }
    }

    public static IEnumerable<T> ExecuteReader<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
        => ExecuteReader<T>(connection, builder, (reader, index) => map(reader), behavior);

    public static IEnumerable<T> ExecuteReader<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map)
        => ExecuteReader<T>(connection, builder, map, CommandBehavior.Default);

    public static async Task<T?> QueryFirstOrDefaultAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var reader = await connection.ExecuteReaderAsync(builder, behavior, cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        return map(reader);
    }

    public static async Task<T?> QueryFirstOrDefaultAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CancellationToken cancellationToken = default)
        => await QueryFirstOrDefaultAsync<T>(connection, builder, map, CommandBehavior.Default, cancellationToken);

    public static async Task<T> QueryFirstAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
        => await QueryFirstOrDefaultAsync<T>(connection, builder, map, behavior, cancellationToken) ?? throw SequenceContainsNoElements();

    public static async Task<T> QueryFirstAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CancellationToken cancellationToken = default)
        => await QueryFirstAsync<T>(connection, builder, map, CommandBehavior.Default, cancellationToken);

    public static async Task<T?> QuerySingleOrDefaultAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
    {
        await using var reader = await connection.ExecuteReaderAsync(builder, behavior, cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return default;
        }

        var first = map(reader);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw SequenceContainsMoreThanOneElement();
        }

        return first;
    }

    public static async Task<T?> QuerySingleOrDefaultAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CancellationToken cancellationToken = default)
        => await QuerySingleOrDefaultAsync<T>(connection, builder, map, CommandBehavior.Default, cancellationToken);

    public static async Task<T> QuerySingleAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior, CancellationToken cancellationToken = default)
        => await QuerySingleOrDefaultAsync<T>(connection, builder, map, behavior, cancellationToken) ?? throw SequenceContainsNoElements();

    public static async Task<T> QuerySingleAsync<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CancellationToken cancellationToken = default)
        => await QuerySingleAsync<T>(connection, builder, map, CommandBehavior.Default, cancellationToken);

    public static T? QueryFirstOrDefault<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        var enumerable = connection.ExecuteReader(builder, map, behavior);

        foreach (var item in enumerable)
        {
            return item;
        }

        return default;
    }

    public static T? QueryFirstOrDefault<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map)
        => QueryFirstOrDefault<T>(connection, builder, map, CommandBehavior.Default);

    public static T QueryFirst<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
        => QueryFirstOrDefault<T>(connection, builder, map, behavior) ?? throw SequenceContainsNoElements();

    public static T QueryFirst<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map)
        => QueryFirst<T>(connection, builder, map, CommandBehavior.Default);

    public static T? QuerySingleOrDefault<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
    {
        var enumerable = connection.ExecuteReader(builder, map, behavior);

        int i = 0;
        T? result = default;
        foreach (var item in enumerable)
        {
            if (i == 0)
            {
                result = item;
                i++;
            }
            else
            {
                throw SequenceContainsMoreThanOneElement();
            }
        }

        return result;
    }

    public static T? QuerySingleOrDefault<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map)
        => QuerySingleOrDefault<T>(connection, builder, map, CommandBehavior.Default);

    public static T QuerySingle<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map, CommandBehavior behavior)
        => QuerySingleOrDefault<T>(connection, builder, map, behavior) ?? throw SequenceContainsNoElements();

    public static T QuerySingle<T>(this DbConnection connection, Action<DbCommand> builder, Func<DbDataReader, T> map)
        => QuerySingle<T>(connection, builder, map, CommandBehavior.Default);

    private static Exception SequenceContainsNoElements()
        => new InvalidOperationException("Sequence contains no elements.");

    private static Exception SequenceContainsMoreThanOneElement()
        => new InvalidOperationException("Sequence contains more than one element.");
}
