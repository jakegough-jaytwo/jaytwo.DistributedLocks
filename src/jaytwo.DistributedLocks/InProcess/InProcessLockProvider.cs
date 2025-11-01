using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.InProcess;

public sealed class InProcessLockProvider : DistributedLockProvider, IDistributedLockProvider
{
    internal const string InProcessProviderName = "InProcess";
    internal const int DefaultLockWaitSecondsFallback = 30;

    private static readonly ConcurrentDictionary<string, Lazy<RefCountedSemaphore>> _semaphores = new(StringComparer.Ordinal);

    public InProcessLockProvider(ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : this(string.Empty, logger, defaultLockWaitSeconds)
    {
    }

    public InProcessLockProvider(string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(InProcessProviderName, lockNamespace, defaultLockWaitSeconds, logger)
    {
        if (defaultLockWaitSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultLockWaitSeconds), "Must be >= 0.");
        }
    }

    public int ActiveLockCount => _semaphores.Count;

    public override async Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default)
        => await CreateLockAsync(resource, waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null, cancellationToken).ConfigureAwait(false);

    public async Task<IDistributedLock> CreateLockAsync(string resource, TimeSpan? waitTime, CancellationToken cancellationToken)
    {
        var lockAttemptId = Guid.NewGuid();
        var qualifiedResource = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var effectiveWaitTime = waitTime ?? TimeSpan.FromSeconds(DefaultLockWaitSeconds);

        // TODO: raw/requested resource, raw/requested waitTime plus qualifiedResource and effectiveWaitTime
        var eventLogger = GetEventLogger(resource, qualifiedResource, lockAttemptId);
        using (eventLogger?.DefaultScope())
        {
            eventLogger?.LogRequested(resource, waitTime, effectiveWaitTime);
            return await CreateLockAsync(qualifiedResource, effectiveWaitTime, eventLogger, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>(await base.HealthCheckAsync(cancellationToken));

        result["status"] = new
        {
            ActiveLockCount,
        };

        return result;
    }

    private static void DecrementWithoutRelease(string name, RefCountedSemaphore refCountedSemaphore)
    {
        lock (refCountedSemaphore)
        {
            if (refCountedSemaphore.Decrement() == 0)
            {
                if (_semaphores.TryGetValue(name, out var currentLazy) &&
                    ReferenceEquals(currentLazy.Value, refCountedSemaphore))
                {
                    _semaphores.TryRemove(name, out _);
                    refCountedSemaphore.Semaphore.Dispose(); // ✅ dispose when last reference goes away
                }
            }
        }
    }

    private async Task<IDistributedLock> CreateLockAsync(string qualifiedResource, TimeSpan waitTime, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qualifiedResource))
        {
            throw new ArgumentException("Provider Resource is required.", nameof(qualifiedResource));
        }

        if (waitTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTime));
        }

        RefCountedSemaphoreReleaser? releaser = null;
        var acquired = false;
        var stopwatch = Stopwatch.StartNew();
        var refCountedSemaphore = GetIncrementedRefCountedSemaphore(qualifiedResource);

        try
        {
            // try/catch because in case the cancellationToken is cancelled and throws an exception after we've created the releaser (and incremented the refCountedSemaphore)
            acquired = await refCountedSemaphore.Semaphore.WaitAsync(waitTime, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            if (acquired)
            {
                releaser = new RefCountedSemaphoreReleaser(qualifiedResource, refCountedSemaphore, releaseOnDispose: true);
            }
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();

            // TODO: whould we downgrade to warning because we re-throw the exception?
            eventLogger?.LogCancelled(stopwatch.Elapsed, ex);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // TODO: whould we downgrade to warning because we re-throw the exception?
            eventLogger?.LogError(stopwatch.Elapsed, ex);
            throw;
        }
        finally
        {
            if (!acquired)
            {
                DecrementWithoutRelease(qualifiedResource, refCountedSemaphore);
            }
        }

        if (!acquired)
        {
            eventLogger?.LogTimedOut(stopwatch.Elapsed);
            return NullLock.Instance;
        }

        eventLogger?.LogAcquired(stopwatch.Elapsed);
        return new InProcessLock(this, releaser!, qualifiedResource, eventLogger);
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
                    new RefCountedSemaphore(new SemaphoreSlim(initialCount: 1))));

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
        private readonly bool _releaseOnDispose;
        private RefCountedSemaphore? _refCountedSemaphore;

        public RefCountedSemaphoreReleaser(string resource, RefCountedSemaphore refCounted, bool releaseOnDispose)
        {
            _name = resource;
            _refCountedSemaphore = refCounted;
            _releaseOnDispose = releaseOnDispose;
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
                    if (_releaseOnDispose)
                    {
                        _refCountedSemaphore.Semaphore.Release();
                    }

                    if (_refCountedSemaphore.Decrement() == 0)
                    {
                        // Remove only if the same object is still in the dictionary
                        if (_semaphores.TryGetValue(_name, out var currentLazy) && ReferenceEquals(currentLazy.Value, _refCountedSemaphore))
                        {
                            _semaphores.TryRemove(_name, out _);
                            _refCountedSemaphore.Semaphore.Dispose(); // dispose on last reference
                        }
                    }
                }

                _refCountedSemaphore = null!;
            }
        }
    }
}
