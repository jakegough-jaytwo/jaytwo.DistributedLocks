using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public class DbConnectionFactory : IDbConnectionFactory
{
    private readonly Func<DbConnection> _connectionFactory;

    public DbConnectionFactory(Func<DbConnection> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public DbConnection CreateConnection() => _connectionFactory();

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => default;

    public DbConnection OpenConnection()
    {
        var connection = CreateConnection();
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection();
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
