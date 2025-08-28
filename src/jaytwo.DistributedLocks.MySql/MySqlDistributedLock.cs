using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public class MySqlDistributedLock : IDistributedLock
{
    private readonly MySqlConnection _connection;

    public MySqlDistributedLock(MySqlConnection connection)
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
        await _connection!.DisposeAsync();
    }
}
