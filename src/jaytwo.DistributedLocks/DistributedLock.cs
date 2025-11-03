using System;
using jaytwo.DistributedLocks.Logging;

namespace jaytwo.DistributedLocks;

public abstract class DistributedLock<TProvider, TResource>
    where TProvider : DistributedLockProvider
{
    public DistributedLock(TProvider provider, TResource providerResource, LockEventLogger? eventLogger)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        //LockAttemptId = lockAttemptId;
        //Resource = string.IsNullOrWhiteSpace(resource) ? throw new ArgumentException("Resource is required.", nameof(resource)) : resource;
        if (providerResource is string providerResourceString && string.IsNullOrEmpty(providerResourceString))
        {
            throw new ArgumentException("Provider Resource is required.", nameof(providerResource));
        }

        ProviderResource = providerResource ?? throw new ArgumentNullException(nameof(providerResource));

        EventLogger = eventLogger;
    }

    public string ProviderName => Provider.ProviderName;

    public string LockNamespace => Provider.LockNamespace;

    //public Guid LockAttemptId { get; }

    //public string Resource { get; }

    public TResource ProviderResource { get; }

    public abstract bool IsAcquired { get; }

    protected LockEventLogger? EventLogger { get; }

    protected TProvider Provider { get; }
}
