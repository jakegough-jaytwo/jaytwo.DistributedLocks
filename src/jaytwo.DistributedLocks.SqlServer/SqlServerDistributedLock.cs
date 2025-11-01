using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Data.SqlClient;

namespace jaytwo.DistributedLocks.SqlServer;

public sealed class SqlServerDistributedLock : DistributedLock<SqlServerDistributedLockProvider, string>, IDistributedLock, IDisposable, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;

    private readonly Stopwatch _lockHeldTimer;

    private bool _isAcquired;
    private int _disposed;

    public SqlServerDistributedLock(
        SqlServerDistributedLockProvider provider,
        SqlConnection connection,
        SqlTransaction transaction,
        string providerResource,
        EventLogger? eventLogger)
        : base(provider, providerResource, eventLogger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _transaction = transaction;

        _lockHeldTimer = Stopwatch.StartNew();

        _isAcquired = true;
    }

    // TODO: connect this to _disposed and connection/transaction state
    public override bool IsAcquired => _isAcquired;

    public void Dispose()
    {
        DoDispose(() =>
        {
            _transaction.Dispose();
            _connection.Dispose();
        });

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        var valueTask = DoDispose(async () =>
        {
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        });

        GC.SuppressFinalize(this);
        return valueTask;
    }

    private void DoDispose(Action disposeCallback)
        => DoDispose(() =>
        {
            disposeCallback();
            return default;
        }).AsTask().GetAwaiter().GetResult();

    private async ValueTask DoDispose(Func<ValueTask> disposeCallback)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _lockHeldTimer.Stop();

        using var loggerScope = EventLogger?.DefaultScope();

        Exception? error = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await disposeCallback().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            error = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            // TODO: human readable result
            if (error != null)
            {
                EventLogger?.LogReleaseFailed(stopwatch.Elapsed, error);
            }
            else
            {
                EventLogger?.LogReleased(stopwatch.Elapsed, _lockHeldTimer.Elapsed);
            }
        }
    }
}
