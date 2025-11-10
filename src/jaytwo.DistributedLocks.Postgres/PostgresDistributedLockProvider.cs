using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Db;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public sealed class PostgresDistributedLockProvider : DbDistributedLockProvider, IDistributedLockProvider
{
    internal const string PostgresProviderName = "Postgres";
    internal const int DefaultLockWaitSecondsFallback = 30;

    public PostgresDistributedLockProvider(string connectionString, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(() => new NpgsqlConnection(connectionString), lockNamespace, logger, defaultLockWaitSeconds)
    {
    }

#if NET8_0_OR_GREATER
    public PostgresDistributedLockProvider(DbDataSource dataSource, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(PostgresProviderName, new DbDataSourcePassthrough(dataSource), lockNamespace, defaultLockWaitSeconds, logger)
    {
    }
#endif

    public PostgresDistributedLockProvider(Func<DbConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(PostgresProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public PostgresDistributedLockProvider(IDbConnectionFactory connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(PostgresProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(string connectionString, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionString, DefaultLockNamespace(logger, PostgresProviderName), logger, defaultLockWaitSeconds);

#if NET8_0_OR_GREATER
    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(DbDataSource dataSource, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(dataSource, DefaultLockNamespace(logger, PostgresProviderName), logger, defaultLockWaitSeconds);
#endif

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(Func<DbConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, PostgresProviderName), logger, defaultLockWaitSeconds);

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(IDbConnectionFactory connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, PostgresProviderName), logger, defaultLockWaitSeconds);

    public override async Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default)
        => await CreateLockAsync(resource, waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null, cancellationToken).ConfigureAwait(false);

    public async Task<IDistributedLock> CreateLockAsync(string resource, TimeSpan? waitTime = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Key is required.", nameof(resource));
        }

        if (waitTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTime));
        }

        var lockAttemptId = Guid.NewGuid();
        var qualifiedResource = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var qualifiedResourceHash = HashStringToLong(qualifiedResource);
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

    internal async Task<IDistributedLock> CreateLockAsync(long lockKey, int effectiveTimeoutMs, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        Func<DbConnection, DbTransaction, CancellationToken, Task<bool>> advisoryLockDelegate;

        if (effectiveTimeoutMs == 0)
        {
            advisoryLockDelegate = (c, t, ct) => PgTryAdvisoryLock(lockKey, c, t, eventLogger, ct);
        }
        else
        {
            advisoryLockDelegate = (c, t, ct) => PgAdvisoryLock(lockKey, effectiveTimeoutMs, c, t, eventLogger, ct);
        }

        return await GetAdvisoryLock(advisoryLockDelegate, lockKey, eventLogger, cancellationToken).ConfigureAwait(false);
    }

    protected override object HealthCheckConnectionStringDetails(string connectionString)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);

        return new
        {
            connectionStringBuilder.Host,
            connectionStringBuilder.Port,
            connectionStringBuilder.Database,
            connectionStringBuilder.Username,
        };
    }

    protected override async Task<object> HealthCheckServerDataAsync(DbConnection connection, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync(
            c => c.WithCommandText("SELECT current_timestamp as current_timestamp, localtimestamp as localtimestamp, cast(inet_server_addr() as VARCHAR) AS inet_server_addr, inet_server_port() AS inet_server_port"),
            r => new
            {
                current_timestamp = DateTime.SpecifyKind(r.GetDateTime("current_timestamp"), DateTimeKind.Unspecified).ToString("O"),
                localtimestamp = DateTime.SpecifyKind(r.GetDateTime("localtimestamp"), DateTimeKind.Unspecified).ToString("O"),
                inet_server_addr = r["inet_server_addr"],
                inet_server_port = r["inet_server_port"],
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<bool> PgTryAdvisoryLock(long key, DbConnection connection, DbTransaction transaction, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "pg_try_advisory_xact_lock")));

        var returnValue = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            returnValue = await connection.ExecuteScalarAsync<bool>(
                command => command
                    .WithTransaction(transaction)
                    .WithCommandText("SELECT pg_try_advisory_xact_lock(@key)")
                    .WithParameter("key", key, DbType.Int64),
                cancellationToken);

            stopwatch.Stop();
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

        if (returnValue)
        {
            eventLogger?.LogAcquired(stopwatch.Elapsed, x => x.AppendToMessage(("result", "acquired"), ("waited", false), ("return_value", returnValue)));
        }
        else
        {
            // TODO: is it valid to say timeout with no wait?
            eventLogger?.LogTimedOut(stopwatch.Elapsed, x => x.AppendToMessage(("result", "not_acquired"), ("waited", false), ("return_value", returnValue)));
        }

        return returnValue;
    }

    private async Task<bool> PgAdvisoryLock(long key, int statementTimeoutMs, DbConnection connection, DbTransaction transaction, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        if (statementTimeoutMs < 0)
        {
            statementTimeoutMs = 0;
        }

        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "pg_advisory_xact_lock")));

        await using var command = connection.CreateCommand()
            .WithTransaction(transaction)
            .WithCommandText("""
                WITH _s AS (
                  SELECT set_config('statement_timeout', @statement_timeout_ms::text, true)
                )
                SELECT pg_advisory_xact_lock(@key);
            """)
            .WithParameter("key", key, DbType.Int64)
            .WithParameter("statement_timeout_ms", statementTimeoutMs)
            .WithCommandTimeout(SecondsCeilingFromMilliseconds(statementTimeoutMs) + 2); // Ensure SQL command timeout won't undercut the statement timeout

        var stopwatch = Stopwatch.StartNew();
        bool acquired;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            acquired = true;
            eventLogger?.LogAcquired(stopwatch.Elapsed, x => x.AppendToMessage(("waited", true), ("result", "completed"), ("return_value", "(void))")));
        }
        catch (PostgresException ex) when (ex.SqlState == "57014")
        {
            stopwatch.Stop();
            acquired = false;
            eventLogger?.LogTimedOut(stopwatch.Elapsed, x => x.AppendToMessage(("waited", true), ("result", "statement_timeout")));
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException or OperationCanceledException)
        {
            // TODO: should this ever even happen? does it risk dangling commands?
            stopwatch.Stop();
            acquired = false;
            eventLogger?.LogTimedOut(stopwatch.Elapsed, x => x.AppendToMessage(("waited", true), ("result", "client_timeout")));
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

        return acquired;
    }

    private async Task<IDistributedLock> GetAdvisoryLock(
        Func<DbConnection, DbTransaction, CancellationToken, Task<bool>> advisoryLockDelegate,
        long key,
        LockEventLogger? eventLogger,
        CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = await ConnectionFactory.OpenConnectionAsync(cancellationToken);
        DbTransaction? transaction = null;

        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            acquired = await advisoryLockDelegate(connection, transaction, cancellationToken);

            if (acquired)
            {
                return new PostgresDistributedLock(this, connection, transaction, key, eventLogger);
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
}
