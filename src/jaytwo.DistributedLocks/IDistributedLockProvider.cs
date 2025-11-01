using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jaytwo.DistributedLocks;

public interface IDistributedLockProvider : IDisposable, IAsyncDisposable
{
    int DefaultLockWaitSeconds { get; }

    Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default);
}
