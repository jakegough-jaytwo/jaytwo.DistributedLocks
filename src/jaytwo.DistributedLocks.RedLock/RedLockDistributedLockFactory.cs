using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks.RedLock;

public class RedLockDistributedLockFactory : IDistributedLockFactory
{
    private global::RedLockNet.IDistributedLockFactory _redLockFactory;

    public RedLockDistributedLockFactory(global::RedLockNet.IDistributedLockFactory redLockFactory, TimeSpan? defaultTimeout = default)
        : this(Guid.NewGuid().ToString(), redLockFactory, defaultTimeout)
    {
    }

    public RedLockDistributedLockFactory(string instanceKey, global::RedLockNet.IDistributedLockFactory redLockFactory, TimeSpan? defaultTimeout = default)
    {
        _redLockFactory = redLockFactory;
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
        InstanceKey = instanceKey;
    }

    public TimeSpan DefaultTimeout { get; set; }

    private string InstanceKey { get; }

    public async Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default)
    {
        var redLockKey = $"{key}.{InstanceKey}";
        var redLock = await _redLockFactory.CreateLockAsync(redLockKey, timeout ?? DefaultTimeout); // TODO: other params
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
#if NET6_0_OR_GREATER
        => ValueTask.CompletedTask;
#else
        => default;
#endif
}
