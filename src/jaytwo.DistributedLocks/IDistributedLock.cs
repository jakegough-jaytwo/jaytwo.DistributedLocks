namespace jaytwo.DistributedLocks;

public interface IDistributedLock : IDisposable, IAsyncDisposable
{
    bool IsAcquired { get; }
}
