using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public class PostgresDistributedLockFactory : IDistributedLockFactory
{
    private NpgsqlConnection _connection;

    public PostgresDistributedLockFactory(NpgsqlConnection connection, TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), connection, defaultTimeout)
    {
    }

    public PostgresDistributedLockFactory(string instanceKey, NpgsqlConnection connection, TimeSpan? defaultTimeout = default)
    {
        _connection = connection;
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultTimeout { get; set; }

    private string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        var hashedKey = HashStringToUInt($"{key}.{InstanceKey}");

        // pg_advisory_lock and pg_advisory_xact_lock just blocks until it acquires the lock
        string query = $"SELECT pg_advisory_xact_lock({hashedKey})";

        var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteScalarAsync<bool>(query, transaction, timeout ?? DefaultTimeout);
            return new PostgresDistributedLock(transaction, true);
        }
        catch
        {
            // no matter how we got here, if the sql execution fails then we for sure did not get the lock
            await transaction.DisposeAsync();
        }

        return NullLock.Instance;
    }

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_connection.ConnectionString);

        var result = new Dictionary<string, object>()
        {
            { "host", connectionStringBuilder.Host! },
            { "port", connectionStringBuilder.Port! },
            { "database", connectionStringBuilder.Database! },
            { "username", connectionStringBuilder.Username! },
        };

        try
        {
            var nowTime = await ExecuteScalarAsync<DateTime>("SELECT now()", cancellationToken: cancellationToken);
            result["serverTime"] = DateTime.SpecifyKind(nowTime, DateTimeKind.Unspecified).ToString("O");
        }
        catch (Exception ex)
        {
            var healthCheckException = new Exception(ex.Message, ex);
            healthCheckException.Data.Add(nameof(result), result);
            throw healthCheckException;
        }

        var testKey = Guid.NewGuid().ToString();
        bool lockAcquired = false;
        await using (var redlock = await CreateLockAsync(testKey, TimeSpan.Zero, cancellationToken))
        {
            lockAcquired = redlock.IsAcquired;
        }

        result.Add("lock_acquired", lockAcquired);

        return result;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    private static uint HashStringToUInt(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hashBytes, 0);
    }

    private async Task<T?> ExecuteScalarAsync<T>(string query, NpgsqlTransaction? transaction = default, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteScalarAsync(query, transaction, timeout, cancellationToken);

        if (result is T value)
        {
            return value;
        }

        return default(T?);
    }

    private async Task InitOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }
    }

    private async Task<object?> ExecuteScalarAsync(string query, NpgsqlTransaction? transaction = default, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        // not disposing the command because it will end the connection
        var command = new NpgsqlCommand(query, _connection)
        {
            CommandTimeout = (int)timeout.TotalSeconds,
            Transaction = transaction,
        };

        await InitOpenConnectionAsync(cancellationToken);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<NpgsqlTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        await InitOpenConnectionAsync(cancellationToken);
        return await _connection.BeginTransactionAsync(cancellationToken);
    }
}
