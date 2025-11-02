using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public sealed class PostgresDistributedLockProvider : DbDistributedLockProvider<NpgsqlConnection>, IDistributedLockProvider
{
    internal const string PostgresProviderName = "Postgres";
    internal const int DefaultLockWaitSecondsFallback = 30;

    public PostgresDistributedLockProvider(string connectionString, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(() => new NpgsqlConnection(connectionString), lockNamespace, logger, defaultLockWaitSeconds)
    {
    }

    public PostgresDistributedLockProvider(NpgsqlDataSource dataSource, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(PostgresProviderName, dataSource.CreateConnection, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public PostgresDistributedLockProvider(Func<NpgsqlConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(PostgresProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(string connectionString, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionString, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(NpgsqlDataSource dataSource, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(dataSource, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public static PostgresDistributedLockProvider CreateWithDefaultLockNamespace(Func<NpgsqlConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

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

    internal async Task<IDistributedLock> CreateLockAsync(long lockKey, int effectiveTimeoutMs, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<bool>> advisoryLockDelegate;

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

    protected override async Task<object> HealthCheckServerDataAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var serverInfo = await QuerySingleAnonymousAsync(
            connection,
            "SELECT CURRENT_TIMESTAMP as time, cast(inet_server_addr() as VARCHAR) AS inet_server_addr, inet_server_port() AS inet_server_port",
            prototype: new { time = default(DateTime), inet_server_addr = default(string), inet_server_port = default(int) },
            cancellationToken);

        return new
        {
            current_timestamp = DateTime.SpecifyKind(serverInfo.time, DateTimeKind.Unspecified).ToString("O"),
            serverInfo.inet_server_addr,
            serverInfo.inet_server_port,
        };
    }

    private async Task<bool> PgTryAdvisoryLock(long key, NpgsqlConnection connection, NpgsqlTransaction transaction, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "pg_try_advisory_xact_lock")));
        const string query = "SELECT pg_try_advisory_xact_lock(@key)";
        var args = new { key, };

        var returnValue = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            returnValue = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(query, args, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
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

    private async Task<bool> PgAdvisoryLock(long key, int statementTimeoutMs, NpgsqlConnection connection, NpgsqlTransaction transaction, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        if (statementTimeoutMs < 0)
        {
            statementTimeoutMs = 0;
        }

        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "pg_advisory_xact_lock")));

        const string query = """
            WITH _s AS (
              SELECT set_config('statement_timeout', @statementTimeoutMs::text, true)
            )
            SELECT pg_advisory_xact_lock(@key);
            """;

        var args = new
        {
            key,
            statementTimeoutMs,
        };

        // Ensure SQL command timeout won't undercut the statement timeout
        int commandTimeoutSeconds = SecondsCeilingFromMilliseconds(statementTimeoutMs) + 2;

        var stopwatch = Stopwatch.StartNew();
        bool acquired;
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(query, args, transaction, commandTimeout: commandTimeoutSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
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
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<bool>> advisoryLockDelegate,
        long key,
        EventLogger? eventLogger,
        CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = ConnectionFactory.Invoke();
        NpgsqlTransaction? transaction = null;

        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false); // Dapper automatically closes connections that it automatically opened
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

    private async Task<T> QuerySingleAnonymousAsync<T>(IDbConnection connection, string sql, T prototype, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<T>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
}
