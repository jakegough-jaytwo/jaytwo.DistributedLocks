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
    private Func<NpgsqlConnection> _connectionFactory;

    // TODO: test instance keys
    // TODO: organize timeouts (do we really want a timespan? postgres and mysql both only use full seconds)

    public PostgresDistributedLockFactory(string connectionString, TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), connectionString, defaultTimeout)
    {
    }

    public PostgresDistributedLockFactory(string instanceKey, string connectionString, TimeSpan? defaultTimeout = default)
        : this(instanceKey, () => CreateConnection(connectionString), defaultTimeout)
    {
    }

    public PostgresDistributedLockFactory(Func<NpgsqlConnection> connectionFactory, TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), connectionFactory, defaultTimeout)
    {
    }

    public PostgresDistributedLockFactory(string instanceKey, Func<NpgsqlConnection> connectionFactory, TimeSpan? defaultTimeout = default)
    {
        _connectionFactory = connectionFactory;
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultTimeout { get; set; }

    private string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        var hashedKey = HashStringToUInt($"{key}.{InstanceKey}");

        if (timeout.HasValue && timeout.Value == TimeSpan.Zero)
        {
            return await PgAdvisoryXactLock(hashedKey, cancellationToken);
        }
        else
        {
            var timeoutSeconds = (int)(timeout?.TotalSeconds ?? DefaultTimeout.TotalSeconds);
            return await PgAdvisoryLock(hashedKey, timeoutSeconds, cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.Invoke();
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);

        var result = new Dictionary<string, object>()
        {
            { "host", connectionStringBuilder.Host! },
            { "port", connectionStringBuilder.Port! },
            { "database", connectionStringBuilder.Database! },
            { "username", connectionStringBuilder.Username! },
        };

        try
        {
            var nowTime = await connection.ExecuteScalarAsync<DateTime>("SELECT now()", cancellationToken: cancellationToken);
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
        await using (var myLock = await CreateLockAsync(testKey, TimeSpan.Zero, cancellationToken))
        {
            lockAcquired = myLock.IsAcquired;
        }

        result.Add("lock_acquired", lockAcquired);

        return result;
    }

    public void Dispose()
    {
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }

    private static uint HashStringToUInt(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hashBytes, 0);
    }

    private static NpgsqlConnection CreateConnection(string connectionString)
    {
        var connectionStringWithPoolingDisabled = GetConnectionStringWithPoolingDisabled(connectionString);
        return new NpgsqlConnection(connectionStringWithPoolingDisabled);
    }

    private static string GetConnectionStringWithPoolingDisabled(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        builder.Pooling = false;
        return builder.ToString();
    }

    private static void EnsureConnectionPoolingDisabled(NpgsqlConnection connection)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        if (builder.Pooling)
        {
            throw new InvalidOperationException("Connection pooling is enabled. Please disable pooling for advisory locks to work correctly.");
        }
    }

    private async Task<IDistributedLock> PgAdvisoryLock(uint hashedKey, int timeoutSeconds, CancellationToken cancellationToken)
    {
        string query = $"SELECT pg_advisory_lock({hashedKey})";

        bool acquired = false;
        var connection = _connectionFactory.Invoke();
        try
        {
            EnsureConnectionPoolingDisabled(connection);
            await connection.ExecuteNonQueryAsync(query, timeoutSeconds, cancellationToken);
            acquired = true;
            return new PostgresDistributedLock(connection);
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            acquired = false;
            return NullLock.Instance;
        }
        finally
        {
            if (!acquired)
            {
                await connection.DisposeAsync();
            }
        }
    }

    private async Task<IDistributedLock> PgAdvisoryXactLock(uint hashedKey, CancellationToken cancellationToken)
    {
        string query = $"SELECT pg_advisory_xact_lock({hashedKey})";

        bool acquired = false;
        var connection = _connectionFactory.Invoke();

        try
        {
            EnsureConnectionPoolingDisabled(connection);
            acquired = await connection.ExecuteScalarAsync<bool>(query, cancellationToken: cancellationToken);
            if (acquired)
            {
                return new PostgresDistributedLock(connection);
            }
        }
        finally
        {
            if (!acquired)
            {
                await connection.DisposeAsync();
            }
        }

        return NullLock.Instance;
    }
}
