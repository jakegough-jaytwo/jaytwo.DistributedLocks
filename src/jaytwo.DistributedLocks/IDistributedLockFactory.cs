using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks;

public interface IDistributedLockFactory : IDisposable, IAsyncDisposable
{
    TimeSpan DefaultTimeout { get; set; }

    Task<IDistributedLock> CreateLockAsync(string key, TimeSpan? timeout = default, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default);
}
