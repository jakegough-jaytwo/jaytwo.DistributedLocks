using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RedLockNet;

namespace jaytwo.DistributedLocks.RedLock;

public class RedLockDistributedLockProvider : IDistributedLockProvider
{
    private IDistributedLockFactory _redLockFactory;

    public RedLockDistributedLockProvider(IDistributedLockFactory redLockFactory, TimeSpan? defaultWaitTime = default)
        : this(Guid.NewGuid().ToString(), redLockFactory, defaultWaitTime)
    {
    }

    public RedLockDistributedLockProvider(string instanceKey, IDistributedLockFactory redLockFactory, TimeSpan? defaultWaitTime = default)
    {
        _redLockFactory = redLockFactory;
        DefaultWaitTime = defaultWaitTime ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultWaitTime { get; set; }

    public string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? waitTime = default, CancellationToken cancellationToken = default)
    {
        var redLockKey = $"{key}.{InstanceKey}";
        var redLock = await _redLockFactory.CreateLockAsync(
            resource: redLockKey,
            expiryTime: TimeSpan.FromSeconds(60),
            waitTime: waitTime ?? DefaultWaitTime,
            retryTime: TimeSpan.FromSeconds(3),
            cancellationToken: cancellationToken);

        if (redLock.IsAcquired)
        {
            return new RedLockDistributedLock(redLock);
        }

        await redLock.DisposeAsync();
        return NullLock.Instance;
    }

    public async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>();

        var testKey = Guid.NewGuid().ToString();
        bool lockAcquired = false;
        await using (var redlock = await CreateLockAsync(testKey, TimeSpan.Zero, cancellationToken))
        {
            lockAcquired = redlock.IsAcquired;
        }

        result.Add("lock_acquired", lockAcquired);

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
