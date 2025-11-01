using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.SqlServer;

public sealed class SqlServerDistributedLockProvider : DbDistributedLockProvider<SqlConnection>, IDistributedLockProvider
{
    internal const string SqlServerProviderName = "SqlServer";
    internal const int DefaultLockWaitSecondsFallback = 30;

    public SqlServerDistributedLockProvider(string connectionString, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(
            () => new SqlConnection(string.IsNullOrWhiteSpace(connectionString)
                ? throw new ArgumentException("Connection string is required.", nameof(connectionString))
                : connectionString),
            lockNamespace,
            logger,
            defaultLockWaitSeconds)
    {
    }

    public SqlServerDistributedLockProvider(Func<SqlConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(SqlServerProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(string connectionString, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionString, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public static SqlServerDistributedLockProvider CreateWithDefaultLockNamespace(Func<SqlConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

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
        var qualifiedResourcePlain = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var qualifiedResourceHash = HashStringToHex(qualifiedResourcePlain); // hashing because sp_getapplock @Resource is only nvarchar(255)
        var effectiveTimeoutMs = GetEffectiveTimeoutMs(waitTime);
        var effectiveWaitTime = TimeSpan.FromMilliseconds(effectiveTimeoutMs);

        var eventLogger = GetEventLogger(resource, qualifiedResourceHash, lockAttemptId);
        using var loggerScope = eventLogger?.DefaultScope();

        eventLogger?.LogRequested(
            resource,
            requestWaitTime: waitTime,
            effectiveWaitTime: effectiveWaitTime,
            extraConfig: x => x.WithFields(
                ("qualified_resource_plain", qualifiedResourcePlain),
                ("qualified_resource_hash", qualifiedResourceHash)));

        return await CreateLockAsync(qualifiedResourceHash, effectiveTimeoutMs, eventLogger, cancellationToken);
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

    internal async Task<IDistributedLock> CreateLockAsync(string qualifiedResourceHash, int effectiveTimeoutMs, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = ConnectionFactory.Invoke();
        SqlTransaction? transaction = null;

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false); // Dapper automatically closes connections that it automatically opened
            transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
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

    protected override async Task<object> HealthCheckServerDataAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var serverInfo = await QuerySingleAnonymousAsync(
            connection,
            "SELECT @@SERVERNAME as servername, CURRENT_TIMESTAMP as time",
            prototype: new { servername = default(string), time = default(DateTime) },
            cancellationToken);

        return new
        {
            current_timestamp = DateTime.SpecifyKind(serverInfo.time, DateTimeKind.Unspecified).ToString("O"),
            serverInfo.servername,
        };
    }

    private async Task<T> QuerySingleAnonymousAsync<T>(IDbConnection connection, string sql, T prototype, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<T>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);

    private async Task<bool> SpGetAppLock(string providerResource, int timeoutMs, SqlConnection connection, SqlTransaction transaction, EventLogger? eventLogger, CancellationToken cancellationToken)
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

        const string lockMode = "Exclusive";
        const string lockOwner = "Transaction";

        var parameters = new DynamicParameters();
        parameters.Add("@Resource", providerResource, DbType.String, size: 255);
        parameters.Add("@LockMode", lockMode, DbType.AnsiString, size: 32);
        parameters.Add("@LockOwner", lockOwner, DbType.AnsiString, size: 32);
        parameters.Add("@LockTimeout", timeoutMs, DbType.Int32);
        parameters.Add("ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

        // Ensure SQL command timeout won't undercut the lock wait
        int commandTimeoutSeconds = SecondsCeilingFromMilliseconds(timeoutMs) + 2;

        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "sp_getapplock")));

        var executionSucceeded = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "sp_getapplock",
                parameters,
                transaction,
                commandTimeout: commandTimeoutSeconds,
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

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

        var returnValue = executionSucceeded ? parameters.Get<int>("ReturnValue") : default;
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
