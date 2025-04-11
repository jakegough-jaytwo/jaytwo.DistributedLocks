namespace jaytwo.DistributedLocks.InMemory;

public class InProcessLockFactory : IDistributedLockFactory
{
    public InProcessLockFactory(TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), defaultTimeout)
    {
    }

    public InProcessLockFactory(string instanceKey, TimeSpan? defaultTimeout = default)
    {
        InstanceKey = instanceKey;
        DefaultWaitTime = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    public TimeSpan DefaultWaitTime { get; set; }

    private string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        var semaphoreKey = $"{key}.{InstanceKey}";

        // not using SemaphoreSlim because I don't want to manage deleting old unused SemaphoreSlim's
        var semaphore = new Semaphore(1, 1, semaphoreKey);
        try
        {
            if (semaphore.WaitOne(timeout ?? DefaultWaitTime))
            {
                return new InProcessLock(true, semaphore);
            }
        }
        catch
        {
            semaphore.Dispose();
        }

        return NullLock.Instance;
    }

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>();
        await Task.CompletedTask;
        return result;
    }

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync()
    {
        return default;
    }
}
