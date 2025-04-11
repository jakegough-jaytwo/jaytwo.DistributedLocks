using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public class PostgresDistributedLock : IDistributedLock
{
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;

    public PostgresDistributedLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        _connection = connection;
        _transaction = transaction;
        IsAcquired = true;
    }

    public bool IsAcquired { get; }

    public void Dispose()
    {
        _transaction.Dispose();
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
