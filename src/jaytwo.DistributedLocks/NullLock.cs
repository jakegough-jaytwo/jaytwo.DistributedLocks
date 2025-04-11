namespace jaytwo.DistributedLocks;

public class NullLock : IDistributedLock
{
    public static NullLock Instance { get; } = new NullLock();

    public bool IsAcquired => false;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }
}
