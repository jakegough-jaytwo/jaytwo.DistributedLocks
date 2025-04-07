using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public class MySqlDistributedLockFactory : IDistributedLockFactory
{
    private Func<MySqlConnection> _connectionFactory;

    public MySqlDistributedLockFactory(Func<MySqlConnection> connectionFactory, TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), connectionFactory, defaultTimeout)
    {
    }

    public MySqlDistributedLockFactory(string instanceKey, Func<MySqlConnection> connectionFactory, TimeSpan? defaultTimeout = default)
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
        var effectiveTimeout = timeout ?? DefaultTimeout;

        string query = $"SELECT GET_LOCK('{hashedKey}', {effectiveTimeout.TotalSeconds})";

        var connection = _connectionFactory.Invoke();
        try
        {
            await connection.ExecuteScalarAsync<bool>(query, cancellationToken);
            return new MySqlDistributedLock(connection, hashedKey, true);
        }
        catch
        {
            // no matter how we got here, if the sql execution fails then we for sure did not get the loc
            await connection.DisposeAsync();
        }

        return NullLock.Instance;
    }

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Invoke();
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connection.ConnectionString);

        var result = new Dictionary<string, object>()
        {
            { "server", connectionStringBuilder.Server! },
            { "port", connectionStringBuilder.Port! },
            { "database", connectionStringBuilder.Database! },
            { "userid", connectionStringBuilder.UserID! },
            { "pooling", connectionStringBuilder.Pooling },
        };

        try
        {
            var nowTime = await connection.ExecuteScalarAsync<DateTime>("SELECT now(6)", cancellationToken: cancellationToken);
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
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }

    private static uint HashStringToUInt(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToUInt32(hashBytes, 0);
    }
}
