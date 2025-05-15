namespace jaytwo.DistributedLocks;

public interface IDistributedLockProvider : IDisposable, IAsyncDisposable
{
    TimeSpan DefaultWaitTime { get; set; }

    Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? waitTime = default, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default);
}
