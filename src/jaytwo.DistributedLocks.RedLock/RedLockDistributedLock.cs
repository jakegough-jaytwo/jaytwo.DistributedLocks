using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;
using RedLockNet;

namespace jaytwo.DistributedLocks.RedLock;

public sealed class RedLockDistributedLock : DistributedLock<RedLockDistributedLockProvider, string>, IDistributedLock, IDisposable, IAsyncDisposable
{
    private readonly IRedLock _redLock;
    private readonly Stopwatch _lockHeldTimer;

    private int _disposed;

    public RedLockDistributedLock(
        RedLockDistributedLockProvider provider,
        IRedLock redLock,
        string providerResource,
        EventLogger? eventLogger)
        : base(provider, providerResource, eventLogger)
    {
        _redLock = redLock;
        _lockHeldTimer = Stopwatch.StartNew();
    }

    public override bool IsAcquired => _redLock.IsAcquired;

    public void Dispose()
    {
        DoDispose(_redLock.Dispose);
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        var valueTask = DoDispose(_redLock.DisposeAsync);
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

        using var loggerScope = EventLogger?.DefaultScope(x => x.WithFields(("redlock_lock_id", _redLock?.LockId)));

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
