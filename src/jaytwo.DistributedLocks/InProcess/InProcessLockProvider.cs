using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.InProcess;

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
        var refCountedSemaphore = GetIncrementedRefCountedSemaphore(semaphoreKey);
        var releaser = new RefCountedSemaphoreReleaser(semaphoreKey, refCountedSemaphore);

        try
        {
            // try/catch because in case the cancellationToken is cancelled and throws an exception after we've created the releaser (and incremented the refCountedSemaphore)
            var acquired = await refCountedSemaphore.Semaphore.WaitAsync(timeout ?? Timeout.InfiniteTimeSpan, cancellationToken);

            if (!acquired)
            {
                releaser.Dispose();
                return NullLock.Instance;
            }

            return new InProcessLock(acquired, releaser);
        }
        catch
        {
            releaser.Dispose();
            throw;
        }
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

    private RefCountedSemaphore GetIncrementedRefCountedSemaphore(string semaphoreKey)
    {
        while (true)
        {
            // The combination of Lazy<T> and ConcurrentDictionary ensures thread-safe creation.
            // The factory may be called by multiple threads, but only one Lazy<T> will be stored.
            // It's safe if multiple Lazy<T> instances are created, as only one is kept.
            // Lazy<T>.Value guarantees the inner RefCountedSemaphore is created only once per Lazy.

            var lazyRefCountedSemaphore = _semaphores.GetOrAdd(semaphoreKey, _ =>
                new Lazy<RefCountedSemaphore>(() =>
                    new RefCountedSemaphore(new SemaphoreSlim(1))));

            var refCountedSemaphore = lazyRefCountedSemaphore.Value;

            lock (refCountedSemaphore)
            {
                // Only increment and return if still tracked in dictionary
                if (_semaphores.TryGetValue(semaphoreKey, out var current) && ReferenceEquals(current.Value, refCountedSemaphore))
                {
                    refCountedSemaphore.Increment();
                    return refCountedSemaphore;
                }
            }

            // The instance was removed after we got it — try again
        }
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
        private readonly object _disposePadlock = new();
        private readonly string _name;
        private RefCountedSemaphore? _refCountedSemaphore;

        public RefCountedSemaphoreReleaser(string name, RefCountedSemaphore refCounted)
        {
            _name = name;
            _refCountedSemaphore = refCounted;
        }

        public void Dispose()
        {
            lock (_disposePadlock)
            {
                if (_refCountedSemaphore == null)
                {
                    return;
                }

                lock (_refCountedSemaphore)
                {
                    _refCountedSemaphore.Semaphore.Release();

                    if (_refCountedSemaphore.Decrement() == 0)
                    {
                        // Remove only if the same object is still in the dictionary
                        if (_semaphores.TryGetValue(_name, out var currentLazy) && ReferenceEquals(currentLazy.Value, _refCountedSemaphore))
                        {
                            _semaphores.TryRemove(_name, out _);
                        }
                    }
                }

                _refCountedSemaphore = null!;
            }
        }
    }
}
