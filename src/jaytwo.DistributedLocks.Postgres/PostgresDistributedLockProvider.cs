using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public class PostgresDistributedLockProvider : IDistributedLockProvider
{
    private Func<NpgsqlConnection> _connectionFactory;

    // TODO: test instance keys
    // TODO: organize timeouts (do we really want a timespan? postgres and mysql both only use full seconds)

    public PostgresDistributedLockProvider(string connectionString, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionString, defaultWaitTime)
    {
    }

    public PostgresDistributedLockProvider(string instanceKey, string connectionString, TimeSpan? defaultWaitTime = default)
        : this(instanceKey, () => new NpgsqlConnection(connectionString), defaultWaitTime)
    {
    }

    public PostgresDistributedLockProvider(Func<NpgsqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionFactory, defaultWaitTime)
    {
    }

    public PostgresDistributedLockProvider(string instanceKey, Func<NpgsqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
    {
        _connectionFactory = connectionFactory;
        DefaultWaitTime = defaultWaitTime ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultWaitTime { get; set; }

    public string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? waitTime = default, CancellationToken cancellationToken = default)
    {
        var hashedKey = HashStringToUInt($"{key}.{InstanceKey}");
        var timeoutSeconds = (int)(waitTime?.TotalSeconds ?? DefaultWaitTime.TotalSeconds);

        if (timeoutSeconds == 0)
        {
            return await PgTryAdvisoryLock(hashedKey, cancellationToken);
        }
        else
        {
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
            var nowTime = await connection.ExecuteScalarAsync<DateTime>(new CommandDefinition("SELECT CURRENT_TIMESTAMP", cancellationToken: cancellationToken));
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

    private static async Task<bool> PgTryAdvisoryLock(uint hashedKey, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        string query = $"SELECT pg_try_advisory_xact_lock({hashedKey})";
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(query, transaction: transaction, cancellationToken: cancellationToken));
    }

    private static async Task<bool> PgAdvisoryLock(uint hashedKey, int timeoutSeconds, NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        string query = $"SELECT pg_advisory_xact_lock({hashedKey})";
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(query, transaction: transaction, commandTimeout: timeoutSeconds, cancellationToken: cancellationToken));
            return true;
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            return false;
        }
    }

    private async Task<IDistributedLock> PgAdvisoryLock(uint hashedKey, int timeoutSeconds, CancellationToken cancellationToken)
        => await GetAdvisoryLock(
            (c, t, ct) => PgAdvisoryLock(hashedKey, timeoutSeconds, c, t, ct),
            cancellationToken);

    private async Task<IDistributedLock> PgTryAdvisoryLock(uint hashedKey, CancellationToken cancellationToken)
        => await GetAdvisoryLock(
            (c, t, ct) => PgTryAdvisoryLock(hashedKey, c, t, ct),
            cancellationToken);

    private async Task<IDistributedLock> GetAdvisoryLock(
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<bool>> advisoryLockDelegate,
        CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = _connectionFactory.Invoke();
        NpgsqlTransaction? transaction = null;

        try
        {
            await connection.OpenAsync(cancellationToken); // Dapper automatically closes connections that it automatically opened
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            acquired = await advisoryLockDelegate(connection, transaction, cancellationToken);

            if (acquired)
            {
                return new PostgresDistributedLock(connection, transaction);
            }
        }
        finally
        {
            if (!acquired)
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }

                await connection.DisposeAsync();
            }
        }

        return NullLock.Instance;
    }
}
