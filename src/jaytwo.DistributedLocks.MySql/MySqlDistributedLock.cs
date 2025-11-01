using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using jaytwo.DistributedLocks.Logging;
using MySql.Data.MySqlClient;

namespace jaytwo.DistributedLocks.MySql;

public sealed class MySqlDistributedLock : DistributedLock<MySqlDistributedLockProvider, string>, IDistributedLock, IDisposable, IAsyncDisposable
{
    private readonly MySqlConnection _connection;

    private readonly Stopwatch _lockHeldTimer;

    private int _disposed;
    private bool _released;

    public MySqlDistributedLock(MySqlDistributedLockProvider provider, MySqlConnection connection, string name, EventLogger? eventLogger)
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

    private void DoDispose(Func<int?> disposeCallback)
        => DoDispose(() => Task.FromResult(disposeCallback())).AsTask().GetAwaiter().GetResult();

    private async ValueTask DoDispose(Func<Task<int?>> disposeCallback)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lockHeldTimer.Stop();

        using var loggerScope = EventLogger?.DefaultScope(x => x.WithFields(("sql_command", "RELEASE_LOCK")));

        int? returnValue = null;
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

    private int? ReleaseLock()
        => _connection.ExecuteScalar<int?>(BuildCommandDefinition());

    private async Task<int?> ReleaseLockAsync()
        => await _connection.ExecuteScalarAsync<int?>(BuildCommandDefinition());

    private CommandDefinition BuildCommandDefinition()
        => new CommandDefinition("SELECT RELEASE_LOCK(@name)", new { name = ProviderResource }, commandTimeout: 5);
}
