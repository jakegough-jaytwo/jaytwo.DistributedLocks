#if NET8_0_OR_GREATER
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public class DbDataSourcePassthrough : IDbConnectionFactory
{
    private readonly DbDataSource _dbDataSource;

    public DbDataSourcePassthrough(DbDataSource dbDataSource)
    {
        _dbDataSource = dbDataSource;
    }

    public DbConnection CreateConnection() => _dbDataSource.CreateConnection();

    public DbConnection OpenConnection() => _dbDataSource.OpenConnection();

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) => await _dbDataSource.OpenConnectionAsync(cancellationToken);
}
#endif
