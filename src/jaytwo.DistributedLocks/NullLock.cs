using System.Threading.Tasks;

namespace jaytwo.DistributedLocks;

public class NullLock : IDistributedLock
{
    public static NullLock Instance { get; } = new NullLock();

    public bool IsAcquired => false;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
#if NET6_0_OR_GREATER
        => ValueTask.CompletedTask;
#else
        => default;
#endif
}
