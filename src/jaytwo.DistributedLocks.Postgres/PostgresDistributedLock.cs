using System.Threading.Tasks;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public class PostgresDistributedLock : IDistributedLock
{
    private NpgsqlTransaction _transaction;

    public PostgresDistributedLock(NpgsqlTransaction transaction, bool acquired)
    {
        _transaction = transaction;
        IsAcquired = acquired;
    }

    public bool IsAcquired { get; }

    public void Dispose()
        => _transaction.Dispose();

    public async ValueTask DisposeAsync()
        => await _transaction!.DisposeAsync();
}
