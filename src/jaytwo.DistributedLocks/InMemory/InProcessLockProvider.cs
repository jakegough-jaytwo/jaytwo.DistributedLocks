using System.Collections.Concurrent;

namespace jaytwo.DistributedLocks.InMemory;

public class InProcessLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<RefCountedSemaphore>> _semaphores = new(StringComparer.Ordinal);

    public InProcessLockProvider(TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), defaultTimeout)
    {
    }

    public InProcessLockProvider(string instanceKey, TimeSpan? defaultTimeout = default)
    {
        InstanceKey = instanceKey;
        DefaultWaitTime = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    public int ActiveLockCount => _semaphores.Count;

    public TimeSpan DefaultWaitTime { get; set; }

    public string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        var semaphoreKey = $"{key}.{InstanceKey}";

        // The lazy plus concurrent dictionary is a thread-safe because inside the concurrent dictionary, the lambda to
        // create an item may get invoked by multiple threads, we're only guaranteed exactly one will be stored and returned
        // by GetOrAdd.  We don't care if a lazy gets created multiple times, as long as the lambda inside the lazy is only
        // called once, which is guaranteed by the Lazy<T> class.

        var lazyRefCountedSemaphore = _semaphores.GetOrAdd(semaphoreKey, key =>
            new Lazy<RefCountedSemaphore>(() =>
                new RefCountedSemaphore(new SemaphoreSlim(1, 1))));

        var refCountedSemaphore = lazyRefCountedSemaphore.Value;
        refCountedSemaphore.Increment();

        bool acquired = false;
        try
        {
            acquired = await refCountedSemaphore.Semaphore.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch
        {
        }

        if (!acquired)
        {
            refCountedSemaphore.Decrement();
            return NullLock.Instance;
        }

        var releaser = new RefCountedSemaphoreReleaser(semaphoreKey, refCountedSemaphore);
        return new InProcessLock(acquired, releaser);
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

    private class RefCountedSemaphore
    {
        private int _refCount = 0;

        public RefCountedSemaphore(SemaphoreSlim semaphore)
        {
            Semaphore = semaphore;
        }

        public SemaphoreSlim Semaphore { get; }

        public void Increment() => Interlocked.Increment(ref _refCount);

        public int Decrement() => Interlocked.Decrement(ref _refCount);
    }

    private sealed class RefCountedSemaphoreReleaser : IDisposable
    {
        private readonly string _name;
        private RefCountedSemaphore? _refCounted;

        public RefCountedSemaphoreReleaser(string name, RefCountedSemaphore refCounted)
        {
            _name = name;
            _refCounted = refCounted;
        }

        public void Dispose()
        {
            if (_refCounted == null)
            {
                return;
            }

            _refCounted.Semaphore.Release();
            if (_refCounted.Decrement() == 0)
            {
                _semaphores.TryRemove(_name, out _);
            }

            _refCounted = null!;
        }
    }
}
