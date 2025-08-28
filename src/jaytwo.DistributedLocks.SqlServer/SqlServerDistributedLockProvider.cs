using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;

namespace jaytwo.DistributedLocks.SqlServer;

public class SqlServerDistributedLockProvider : IDistributedLockProvider
{
    private Func<SqlConnection> _connectionFactory;

    public SqlServerDistributedLockProvider(string connectionString, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionString, defaultWaitTime)
    {
    }

    public SqlServerDistributedLockProvider(string instanceKey, string connectionString, TimeSpan? defaultWaitTime = default)
        : this(instanceKey, () => new SqlConnection(connectionString), defaultWaitTime)
    {
    }

    public SqlServerDistributedLockProvider(Func<SqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), connectionFactory, defaultWaitTime)
    {
    }

    public SqlServerDistributedLockProvider(string instanceKey, Func<SqlConnection> connectionFactory, TimeSpan? defaultWaitTime = default)
    {
        _connectionFactory = connectionFactory;
        DefaultWaitTime = defaultWaitTime ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultWaitTime { get; set; }

    public string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? waitTime = default, CancellationToken cancellationToken = default)
    {
        var hashedKey = HashString($"{key}.{InstanceKey}"); // is hashing really necessary? in postgres we hash to produce an int, in mysql we hash to avoid sql injection
        var timeoutMs = (int)Math.Ceiling(waitTime?.TotalMilliseconds ?? DefaultWaitTime.TotalMilliseconds);

        bool acquired = false;
        var connection = _connectionFactory.Invoke();
        SqlTransaction? transaction = null;

        try
        {
            await connection.OpenAsync(cancellationToken); // Dapper automatically closes connections that it automatically opened
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            acquired = await SpGetAppLock(hashedKey, timeoutMs, connection, transaction, cancellationToken);

            if (acquired)
            {
                return new SqlServerDistributedLock(connection, transaction);
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

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.Invoke();
        var connectionStringBuilder = new SqlConnectionStringBuilder(connection.ConnectionString);

        var result = new Dictionary<string, object>()
        {
            { nameof(SqlConnectionStringBuilder.DataSource), connectionStringBuilder.DataSource! },
            { nameof(SqlConnectionStringBuilder.InitialCatalog), connectionStringBuilder.InitialCatalog! },
            { nameof(SqlConnectionStringBuilder.UserID), connectionStringBuilder.UserID! },
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

    public ValueTask DisposeAsync()
    {
        return default;
    }

    private static string HashString(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes, 0);
    }

    private static async Task<bool> SpGetAppLock(string resource, int timeoutMs, SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@Resource", resource);
        parameters.Add("@LockMode", "Exclusive");
        parameters.Add("@LockOwner", "Session");
        parameters.Add("@LockTimeout", timeoutMs);
        parameters.Add("ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

        await connection.ExecuteAsync(new CommandDefinition(
            "sp_getapplock",
            parameters,
            transaction,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        var result = parameters.Get<int>("ReturnValue");

        return result == 0;
    }
}
