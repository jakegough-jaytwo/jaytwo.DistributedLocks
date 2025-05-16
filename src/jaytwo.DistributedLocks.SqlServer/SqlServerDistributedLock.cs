using Microsoft.Data.SqlClient;

namespace jaytwo.DistributedLocks.SqlServer;

public class SqlServerDistributedLock : IDistributedLock
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;

    public SqlServerDistributedLock(SqlConnection connection, SqlTransaction transaction)
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
