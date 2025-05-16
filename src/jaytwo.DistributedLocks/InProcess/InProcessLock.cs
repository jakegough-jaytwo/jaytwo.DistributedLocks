namespace jaytwo.DistributedLocks.InProcess;

public class InProcessLock : IDistributedLock
{
    private IDisposable? _releaser;

    public InProcessLock(bool acquired, IDisposable releaser)
    {
        IsAcquired = acquired;
        _releaser = releaser;
    }

    public bool IsAcquired { get; }

    public void Dispose()
    {
        _releaser?.Dispose();
        _releaser = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return default;
    }
}
