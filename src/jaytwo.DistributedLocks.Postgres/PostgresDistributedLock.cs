using System.Threading.Tasks;
using Npgsql;

namespace jaytwo.DistributedLocks.Postgres;

public class PostgresDistributedLock : IDistributedLock
{
    private NpgsqlConnection _connection;

    public PostgresDistributedLock(NpgsqlConnection connection)
    {
        _connection = connection;
        IsAcquired = true;
    }

    public bool IsAcquired { get; }

    public void Dispose()
    {
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
