using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public sealed class MySqlDistributedLockProvider : DbDistributedLockProvider<MySqlConnection>, IDistributedLockProvider
{
    internal const string MySqlProviderName = "MySql";
    internal const int DefaultLockWaitSecondsFallback = 30;

    public MySqlDistributedLockProvider(string connectionString, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(() => new MySqlConnection(connectionString), lockNamespace, logger, defaultLockWaitSeconds)
    {
    }

    public MySqlDistributedLockProvider(Func<MySqlConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(MySqlProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
        if (string.IsNullOrEmpty(lockNamespace))
        {
            throw new ArgumentException(
                "No lock namespace provided. Provide a LockNamespace or use CreateWithDefaultLockNamespace().",
                nameof(lockNamespace));
        }
    }

    public static MySqlDistributedLockProvider CreateWithDefaultLockNamespace(string connectionString, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionString, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public static MySqlDistributedLockProvider CreateWithDefaultLockNamespace(Func<MySqlConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public override async Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            throw new ArgumentException("Resource is required.", nameof(resource));
        }

        if (waitSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        }

        var lockAttemptId = Guid.NewGuid();
        var qualifiedResourcePlain = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var qualifiedResourceHash = HashStringToHex(qualifiedResourcePlain); // hashing to avoid 64 char limit in mysql
        var effectiveWaitSeconds = waitSeconds ?? DefaultLockWaitSeconds;
        var effectiveWaitTime = TimeSpan.FromSeconds(effectiveWaitSeconds);

        var eventLogger = GetEventLogger(resource, qualifiedResourceHash, lockAttemptId);
        using var loggerScope = eventLogger?.DefaultScope();

        eventLogger?.LogRequested(
            resource,
            requestWaitTime: waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null,
            effectiveWaitTime: effectiveWaitTime,
            extraConfig: x => x.WithFields(
                ("qualified_resource_plain", qualifiedResourcePlain),
                ("qualified_resource_hash", qualifiedResourceHash)));

        return await CreateLockAsync(qualifiedResourceHash, effectiveWaitSeconds, eventLogger, cancellationToken);
    }

    internal async Task<IDistributedLock> CreateLockAsync(string name, int effectiveWaitSeconds, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = ConnectionFactory.Invoke();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false); // Dapper automatically closes connections that it automatically opened

            acquired = await GetLockAsync(connection, name, effectiveWaitSeconds, eventLogger, cancellationToken).ConfigureAwait(false);

            if (acquired)
            {
                return new MySqlDistributedLock(this, connection, name, eventLogger);
            }
        }
        finally
        {
            if (!acquired)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }

        return NullLock.Instance;
    }

    protected override object HealthCheckConnectionStringDetails(string connectionString)
    {
        var connectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);

        return new
        {
            connectionStringBuilder.Server,
            connectionStringBuilder.Port,
            connectionStringBuilder.Database,
            connectionStringBuilder.UserID,
            connectionStringBuilder.Pooling,
        };
    }

    protected override async Task<object> HealthCheckServerDataAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        var serverInfo = await QuerySingleAnonymousAsync(
            connection,
            "SELECT current_timestamp as time, @@hostname as hostname, @@port as port",
            prototype: new { time = default(DateTime), hostname = default(string), port = default(ulong) },
            cancellationToken);

        return new
        {
            current_timestamp = DateTime.SpecifyKind(serverInfo.time, DateTimeKind.Unspecified).ToString("O"),
            serverInfo.hostname,
            serverInfo.port,
        };
    }

    private async Task<bool> GetLockAsync(MySqlConnection connection, string name, int timeoutSeconds, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "GET_LOCK")));
        const string query = "SELECT GET_LOCK(@name, @timeout)";

        var args = new
        {
            name = name,
            timeout = timeoutSeconds,
        };

        // Ensure SQL command timeout won't undercut the lock timeout
        int commandTimeoutSeconds = timeoutSeconds + 2;

        int? returnValue = null;
        string? result = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            returnValue = await connection.ExecuteScalarAsync<int?>(new CommandDefinition(query, args, commandTimeout: commandTimeoutSeconds, cancellationToken: cancellationToken)).ConfigureAwait(false);
            stopwatch.Stop();

            result = returnValue switch
            {
                0 => "timeout/not acquired",
                1 => "acquired",
                _ => "error",
            };
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

        if (returnValue == 1)
        {
            eventLogger?.LogAcquired(stopwatch.Elapsed, x => x.AppendToMessage(("return_value", returnValue), ("result", result)));
            return true;
        }
        else
        {
            // TODO: is it valid to say timeout with no wait?
            eventLogger?.LogTimedOut(stopwatch.Elapsed, x => x.AppendToMessage(("return_value", returnValue), ("result", result)));
            return false;
        }
    }

    private async Task<T> QuerySingleAnonymousAsync<T>(IDbConnection connection, string sql, T prototype, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync<T>(new CommandDefinition(sql, cancellationToken: cancellationToken)).ConfigureAwait(false);
}
