using System;
using System.Diagnostics;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;

namespace jaytwo.DistributedLocks.InProcess;

public sealed class InProcessLock : DistributedLock<InProcessLockProvider, string>, IDistributedLock, IDisposable, IAsyncDisposable
{
    private readonly IDisposable? _releaser;
    private readonly Stopwatch _lockHeldTimer;

    private bool _isAcquired;
    private volatile bool _disposed;

    public InProcessLock(InProcessLockProvider provider, IDisposable releaser, string providerResource, LockEventLogger? eventLogger)
        : base(provider, providerResource, eventLogger)
    {
        _releaser = releaser;

        _lockHeldTimer = Stopwatch.StartNew();
        _isAcquired = true;
    }

    public override bool IsAcquired => _isAcquired;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lockHeldTimer.Stop();

        using (EventLogger?.DefaultScope())
        {
            var stopwatch = Stopwatch.StartNew();
            _releaser?.Dispose();
            stopwatch.Stop();

            _isAcquired = false;
            EventLogger?.LogReleased(stopwatch.Elapsed, _lockHeldTimer.Elapsed);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
