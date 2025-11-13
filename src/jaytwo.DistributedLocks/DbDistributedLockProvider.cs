using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.Ergonomics.Ado;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks;

public abstract class DbDistributedLockProvider : DistributedLockProvider, IDistributedLockProvider
{
    protected DbDistributedLockProvider(string providerName, Func<DbConnection> connectionFactory, string lockNamespace, int defaultLockWaitSeconds, ILogger? logger)
        : this(providerName, new DbConnectionFactory(connectionFactory), lockNamespace, defaultLockWaitSeconds, logger)
    {
    }

    protected DbDistributedLockProvider(string providerName, IDbConnectionFactory connectionFactory, string lockNamespace, int defaultLockWaitSeconds, ILogger? logger)
        : base(providerName, lockNamespace, defaultLockWaitSeconds, logger)
    {
        if (connectionFactory == null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        ConnectionFactory = connectionFactory;
    }

    protected IDbConnectionFactory ConnectionFactory { get; }

    public override async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>(await base.HealthCheckAsync(cancellationToken));

        await using var connection = await ConnectionFactory.OpenConnectionAsync();

        result["connection"] = HealthCheckConnectionStringDetails(connection.ConnectionString);

        try
        {
            result["from_server"] = await HealthCheckServerDataAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var healthCheckException = new Exception(ex.Message, ex);
            healthCheckException.Data.Add(nameof(result), result);
            throw healthCheckException;
        }

        return result;
    }

    protected internal static int SecondsCeilingFromMilliseconds(double ms)
        => (int)Math.Ceiling(ms / 1000d);

    protected abstract object HealthCheckConnectionStringDetails(string connectionString);

    protected abstract Task<object> HealthCheckServerDataAsync(DbConnection connection, CancellationToken cancellationToken);
}
