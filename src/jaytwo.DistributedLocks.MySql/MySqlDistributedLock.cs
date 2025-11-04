using System;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Db;
using jaytwo.DistributedLocks.Logging;

namespace jaytwo.DistributedLocks.MySql;

public sealed class MySqlDistributedLock : DistributedLock<MySqlDistributedLockProvider, string>, IDistributedLock, IDisposable, IAsyncDisposable
{
    private readonly DbConnection _connection;

    private readonly Stopwatch _lockHeldTimer;

    private int _disposed;
    private bool _released;

    public MySqlDistributedLock(MySqlDistributedLockProvider provider, DbConnection connection, string name, LockEventLogger? eventLogger)
        : base(provider, name, eventLogger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _lockHeldTimer = Stopwatch.StartNew();
    }

    public override bool IsAcquired => !Released;

    public bool Released => Volatile.Read(ref _released);

    public void Dispose()
    {
        DoDispose(ReleaseLock);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return DoDispose(ReleaseLockAsync);
    }

    private void DoDispose(Func<long?> disposeCallback)
        => DoDispose(() => Task.FromResult(disposeCallback())).AsTask().GetAwaiter().GetResult();

    private async ValueTask DoDispose(Func<Task<long?>> disposeCallback)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lockHeldTimer.Stop();

        using var loggerScope = EventLogger?.DefaultScope(x => x.WithFields(("sql_command", "RELEASE_LOCK")));

        long? returnValue = null;
        Exception? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            returnValue = await disposeCallback().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            Volatile.Write(ref _released, returnValue == 1);

            // TODO: human readable result
            if (error != null)
            {
                EventLogger?.LogReleaseFailed(stopwatch.Elapsed, error, x => x.AppendToMessage(("return_value", returnValue)));
            }
            else
            {
                EventLogger?.LogReleased(stopwatch.Elapsed, _lockHeldTimer.Elapsed, x => x.AppendToMessage(("return_value", returnValue)));
            }
        }
    }

    private long? ReleaseLock()
    {
        using var command = BuildCommand();
        return (long?)command.ExecuteScalar();
    }

    private async Task<long?> ReleaseLockAsync()
    {
        await using var command = BuildCommand();
        return (long?)(await command.ExecuteScalarAsync());
    }

    private DbCommand BuildCommand()
        => _connection.CreateCommand()
            .WithCommandText("SELECT RELEASE_LOCK(@name)")
            .WithParameter("name", ProviderResource)
            .WithCommandTimeout(5);
}
