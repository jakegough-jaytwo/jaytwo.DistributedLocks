using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public class MySqlDistributedLockProvider : IDistributedLockProvider
{
    private Func<MySqlConnection> _connectionFactory;

    public MySqlDistributedLockProvider(string connectionString, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionString, defaultWaitTime)
    {
    }

    public MySqlDistributedLockProvider(string instanceKey, string connectionString, TimeSpan? defaultWaitTime = default)
        : this(instanceKey, () => CreateConnection(connectionString), defaultWaitTime)
    {
    }

    public MySqlDistributedLockProvider(Func<MySqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionFactory, defaultWaitTime)
    {
    }

    public MySqlDistributedLockProvider(string instanceKey, Func<MySqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
    {
        _connectionFactory = connectionFactory;
        DefaultWaitTime = defaultWaitTime ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultWaitTime { get; set; }

    private string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? waitTime = default, CancellationToken cancellationToken = default)
    {
        var hashedKey = HashStringToUInt($"{key}.{InstanceKey}");
        var effectiveTimeout = waitTime ?? DefaultWaitTime;

        string query = $"SELECT GET_LOCK('{hashedKey}', {effectiveTimeout.TotalSeconds})";

        var connection = _connectionFactory.Invoke();
        bool acquired = false;
        try
        {
            EnsureConnectionPoolingDisabled(connection);

            await connection.OpenAsync(cancellationToken); // Dapper automatically closes connections that it automatically opened
            acquired = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(query, cancellationToken: cancellationToken));

            if (acquired)
            {
                return new MySqlDistributedLock(connection);
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
            var nowTime = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT now(6)", cancellationToken: cancellationToken));
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

    private static MySqlConnection CreateConnection(string connectionString)
    {
        var connectionStringWithPoolingDisabled = GetConnectionStringWithPoolingDisabled(connectionString);
        return new MySqlConnection(connectionStringWithPoolingDisabled);
    }

    private static string GetConnectionStringWithPoolingDisabled(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        builder.Pooling = false;
        return builder.ToString();
    }

    private static void EnsureConnectionPoolingDisabled(MySqlConnection connection)
    {
        var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);
        if (builder.Pooling)
        {
            throw new InvalidOperationException("Connection pooling is enabled. Please disable pooling for advisory locks to work correctly.");
        }
    }
}
