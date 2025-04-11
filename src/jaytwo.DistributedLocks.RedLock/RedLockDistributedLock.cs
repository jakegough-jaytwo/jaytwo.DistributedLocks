using RedLockNet;

namespace jaytwo.DistributedLocks.RedLock;

public class RedLockDistributedLock : IDistributedLock
{
    private IRedLock _redLock;

    public RedLockDistributedLock(IRedLock redLock)
    {
        _redLock = redLock;
    }

    public bool IsAcquired => _redLock.IsAcquired;

    public void Dispose()
        => _redLock.Dispose();

    public async ValueTask DisposeAsync()
        => await _redLock.DisposeAsync();
}
