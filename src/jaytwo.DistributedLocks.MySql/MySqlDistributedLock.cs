using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public class MySqlDistributedLock : IDistributedLock
{
    private readonly MySqlConnection _connection;
    private readonly uint _lockId;

    public MySqlDistributedLock(MySqlConnection connection, uint lockId, bool acquired)
    {
        _connection = connection;
        _lockId = lockId;
        IsAcquired = acquired;
    }

    public bool IsAcquired { get; }

    public void Dispose()
    {
        if (_connection.IsPoolingEnabled())
        {
            var query = GetReleaseLockQuery();
            _connection.ExecuteScalar(query);
        }

        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsPoolingEnabled())
        {
            var query = GetReleaseLockQuery();
            await _connection.ExecuteScalarAsync(query);
        }

        await _connection!.DisposeAsync();
    }

    private string GetReleaseLockQuery()
        => $"SELECT RELEASE_LOCK('{_lockId}')";
}
