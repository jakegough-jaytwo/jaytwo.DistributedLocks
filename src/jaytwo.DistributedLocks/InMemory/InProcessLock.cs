using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.InMemory;

public class InProcessLock : IDistributedLock
{
    private Semaphore _semaphore;

    public InProcessLock(bool acquired, Semaphore semaphore)
    {
        IsAcquired = acquired;
        _semaphore = semaphore;
    }

    public bool IsAcquired { get; }

    public void Dispose()
    {
        _semaphore.Release();
        _semaphore.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();

#if NET6_0_OR_GREATER
        return ValueTask.CompletedTask;
#else
        return default;
#endif
    }
}
