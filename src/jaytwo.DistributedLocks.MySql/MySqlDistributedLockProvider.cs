using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Db;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.MySql;

public sealed class MySqlDistributedLockProvider : DbDistributedLockProvider, IDistributedLockProvider
{
    internal const string MySqlProviderName = "MySql";
    internal const int DefaultLockWaitSecondsFallback = 30;

#if NET8_0_OR_GREATER
    public MySqlDistributedLockProvider(DbDataSource dataSource, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(MySqlProviderName, new DbDataSourcePassthrough(dataSource), lockNamespace, defaultLockWaitSeconds, logger)
    {
    }
#endif

    public MySqlDistributedLockProvider(Func<DbConnection> connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(MySqlProviderName, new DbConnectionFactory(connectionFactory), lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    public MySqlDistributedLockProvider(IDbConnectionFactory connectionFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(MySqlProviderName, connectionFactory, lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

#if NET8_0_OR_GREATER
    public static MySqlDistributedLockProvider CreateWithDefaultLockNamespace(DbDataSource dataSource, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(dataSource, DefaultLockNamespace(logger, MySqlProviderName), logger, defaultLockWaitSeconds);
#endif

    public static MySqlDistributedLockProvider CreateWithDefaultLockNamespace(Func<DbConnection> connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, MySqlProviderName), logger, defaultLockWaitSeconds);

    public static MySqlDistributedLockProvider CreateWithDefaultLockNamespace(IDbConnectionFactory connectionFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(connectionFactory, DefaultLockNamespace(logger, MySqlProviderName), logger, defaultLockWaitSeconds);

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
        var qualifiedResource = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var qualifiedResourceHash = HashStringToHex(qualifiedResource); // hashing to avoid 64 char limit in mysql
        var effectiveWaitSeconds = waitSeconds ?? DefaultLockWaitSeconds;
        var effectiveWaitTime = TimeSpan.FromSeconds(effectiveWaitSeconds);

        var eventLogger = GetEventLogger(resource, qualifiedResourceHash, lockAttemptId);
        using var loggerScope = eventLogger?.DefaultScope();

        eventLogger?.LogRequested(
            resource,
            requestWaitTime: waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null,
            effectiveWaitTime: effectiveWaitTime,
            extraConfig: x => x.WithFields(
                ("qualified_resource", qualifiedResource),
                ("qualified_resource_hash", qualifiedResourceHash)));

        return await CreateLockAsync(qualifiedResourceHash, effectiveWaitSeconds, eventLogger, cancellationToken);
    }

    internal async Task<IDistributedLock> CreateLockAsync(string name, int effectiveWaitSeconds, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        bool acquired = false;
        var connection = await ConnectionFactory.OpenConnectionAsync(cancellationToken);
        try
        {
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
        var connectionStringBuilder = new DbConnectionStringBuilder { ConnectionString = connectionString };

        string? host = GetFirst(connectionStringBuilder, "Server", "Host", "Data Source", "DataSource", "Addr", "Address", "Network Address");
        string? database = GetFirst(connectionStringBuilder, "Database", "Initial Catalog");
        string? user = GetFirst(connectionStringBuilder, "User Id", "Uid", "Username", "UserID", "User Name");
        int? port = TryGetInt(connectionStringBuilder, out var p, "Port") ? p : null;

        return new { Server = host, Port = port, Database = database, UserID = user };

        static string? GetFirst(DbConnectionStringBuilder b, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (b.TryGetValue(k, out var value) && value is not null)
                {
                    return value.ToString();
                }
            }

            return null;
        }

        static bool TryGetInt(DbConnectionStringBuilder b, out int value, params string[] keys)
        {
            foreach (var k in keys)
            {
                if (b.TryGetValue(k, out var v) && v is not null && int.TryParse(v.ToString(), out value))
                {
                    return true;
                }
            }

            value = default;
            return false;
        }
    }

    protected override async Task<object> HealthCheckServerDataAsync(DbConnection connection, CancellationToken cancellationToken)
        => await connection.QuerySingleAsync(
            c => c.WithCommandText("SELECT current_timestamp as time, @@hostname as hostname, @@port as port"),
            r => new
            {
                current_timestamp = DateTime.SpecifyKind(r.GetDateTime("time"), DateTimeKind.Unspecified).ToString("O"),
                hostname = r["hostname"],
                port = r["port"],
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<bool> GetLockAsync(DbConnection connection, string name, int timeoutSeconds, LockEventLogger? eventLogger, CancellationToken cancellationToken)
    {
        using var loggerScope = eventLogger?.Scope(x => x.WithFields(("sql_command", "GET_LOCK")));

        long? returnValue = null;
        string? result = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            returnValue = await connection.ExecuteScalarAsync<long>(
                command => command
                    .WithCommandText("SELECT GET_LOCK(@name, @timeout)")
                    .WithParameter("name", name)
                    .WithParameter("timeout", timeoutSeconds)
                    .WithCommandTimeout(timeoutSeconds + 2), // Ensure SQL command timeout won't undercut the lock timeout
                cancellationToken);

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
}
