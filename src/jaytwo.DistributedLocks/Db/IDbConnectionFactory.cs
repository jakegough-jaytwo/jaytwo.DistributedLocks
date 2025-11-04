using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.Db;

public interface IDbConnectionFactory
{
    public DbConnection CreateConnection();

    public DbConnection OpenConnection();

    public Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
