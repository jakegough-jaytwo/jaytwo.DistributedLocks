using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Db;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.SqlServer;

public sealed class SqlServerDistributedLockProvider : DbDistributedLockProvider, IDistributedLockProvider
{
    internal const string SqlServerProviderName = "SqlServer";
    internal const int DefaultLockWaitSecondsFallback = 30;

    public SqlServerDistributedLockProvider(string connectionString, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(
            new DbConnectionFactory(() => new SqlConnection(connectionString)),
            lockNamespace,
            logger,
            defaultLockWaitSeconds)
    {
    }

#if NET8_0_OR_GREATER
    public SqlServerDistributedLockProvider(DbDataSource dataSource, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(SqlServerProviderName, new DbDataSourcePassthrough(dataSource), lockNamespace, defaultLockWaitSeconds, logger)
    {
    }
#endif

    public SqlServerDistributedLockProvider(Func<DbConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(SqlServerProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public SqlServerDistributedLockProvider(IDbConnectionFactory connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(SqlServerProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(string connectionString, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionString, DefaultLockNamespace(logger, SqlServerProviderName), logger, defaultLockWaitSeconds);

#if NET8_0_OR_GREATER
    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(DbDataSource dataSource, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(dataSource, DefaultLockNamespace(logger, SqlServerProviderName), logger, defaultLockWaitSeconds);
#endif

    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(Func<DbConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, SqlServerProviderName), logger, defaultLockWaitSeconds);

    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(IDbConnectionFactory connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, SqlServerProviderName), logger, defaultLockWaitSeconds);

    public override async Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default)
        => await CreateLockAsync(resource, waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null, cancellationToken).ConfigureAwait(false);

    public async Task<IDistributedLock> CreateLockAsync(string resource, TimeSpan? waitTime, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource is required.", nameof(resource));
        }

        if (waitTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTime));
        }

        var lockAttemptId = Guid.NewGuid();
        var qualifiedResource = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var qualifiedResourceHash = HashStringToHex(qualifiedResource); // hashing because sp_getapplock @Resource is only nvarchar(255)
        var effectiveWaitMs = GetEffectiveWaitMs(waitTime);
        var effectiveWaitTime = TimeSpan.FromMilliseconds(effectiveWaitMs);

        var eventLogger = GetEventLogger(resource, qualifiedResourceHash, lockAttemptId);
        using var loggerScope = eventLogger?.DefaultScope();

        eventLogger?.LogRequested(
            resource,
            requestWaitTime: waitTime,
            effectiveWaitTime: effectiveWaitTime,
            extraConfig: x => x.WithFields(
                ("qualified_resource", qualifiedResource),
                ("qualified_resource_hash", qualifiedResourceHash)));

        return await CreateLockAsync(qualifiedResourceHash, effectiveWaitMs, eventLogger, cancellationToken);
    }

    internal static (bool Acquired, bool Waited, string Result) AppLockResultFromReturnValue(bool executionSucceeded, int returnValue)
    {
        if (!executionSucceeded)
        {
            return (false, false, "error");
        }

        return returnValue switch
        {
            0 => (true, false, "granted_immediately"),
            1 => (true, true, "granted_after_waiting"),
            -1 => (false, false, "timeout"),
            -2 => (false, false, "canceled"),
            -3 => (false, false, "deadlock_victim"),
            _ => (false, false, $"unknown ({returnValue})"),
        };
    }

    internal async Task<IDistributedLock> CreateLockAsync(string qualifiedResourceHash, int effectiveTimeoutMs, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = await ConnectionFactory.OpenConnectionAsync(cancellationToken);
        DbTransaction? transaction = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            acquired = await SpGetAppLock(qualifiedResourceHash, effectiveTimeoutMs, connection, transaction, eventLogger, cancellationToken).ConfigureAwait(false);

            if (acquired)
            {
                return new SqlServerDistributedLock(this, connection, transaction, qualifiedResourceHash, eventLogger);
            }
        }
        finally
        {
            if (!acquired)
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }

                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        return NullLock.Instance;
    }

    protected override object HealthCheckConnectionStringDetails(string connectionString)
    {
        var connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);

        return new
        {
            connectionStringBuilder.DataSource,
            connectionStringBuilder.InitialCatalog,
            connectionStringBuilder.UserID,
        };
    }

    protected override async Task<object> HealthCheckServerDataAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand()
            .WithCommandText("SELECT @@SERVERNAME as servername, CURRENT_TIMESTAMP as time");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new Exception("Health check query returned no rows.");
        }

        return new
        {
            current_timestamp = DateTime.SpecifyKind(reader.GetFieldValue<DateTime>("time"), DateTimeKind.Unspecified).ToString("O"),
            servername = reader["servername"],
        };
    }

    private async Task<bool> SpGetAppLock(string providerResource, int timeoutMs, DbConnection connection, DbTransaction transaction, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        if (providerResource is null)
        {
            throw new ArgumentNullException(nameof(providerResource));
        }

        if (providerResource.Length > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(providerResource), providerResource, "sp_getapplock @Resource max length is 255.");
        }

        if (timeoutMs < 0)
        {
            timeoutMs = 0;
        }

        const string sqlCommand = "sp_getapplock";
        const string lockMode = "Exclusive";
        const string lockOwner = "Transaction";

        await using var command = connection.CreateCommand()
            .WithTransaction(transaction)
            .WithCommandType(CommandType.StoredProcedure)
            .WithCommandText("sp_getapplock")
            .WithParameter("@Resource", providerResource, DbType.String, size: 255)
            .WithParameter("@LockMode", lockMode, DbType.AnsiString, size: 32)
            .WithParameter("@LockOwner", lockOwner, DbType.AnsiString, size: 32)
            .WithParameter("@LockTimeout", timeoutMs, DbType.Int32)
            .WithParameter("@ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue)
            .WithCommandTimeout(SecondsCeilingFromMilliseconds(timeoutMs) + 2); // Ensure SQL command timeout won't undercut the lock timeout

        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", sqlCommand)));

        var executionSucceeded = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            executionSucceeded = true;
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            eventLogger?.LogCancelled(stopwatch.Elapsed, ex);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            eventLogger?.LogError(stopwatch.Elapsed, ex);
            throw;
        }

        var returnValue = executionSucceeded ? (int)command.Parameters["@ReturnValue"].Value! : default;
        var appLockResult = AppLockResultFromReturnValue(executionSucceeded, returnValue);

        if (appLockResult.Acquired)
        {
            eventLogger?.LogAcquired(stopwatch.Elapsed, x => x.AppendToMessage(("result", appLockResult.Result), ("waited", appLockResult.Waited), ("return_value", returnValue)));
        }
        else
        {
            // TODO: is it valid to say timeout with no wait?
            eventLogger?.LogTimedOut(stopwatch.Elapsed, x => x.AppendToMessage(("result", appLockResult.Result), ("waited", appLockResult.Waited), ("return_value", returnValue)));
        }

        return appLockResult.Acquired;
    }
}
