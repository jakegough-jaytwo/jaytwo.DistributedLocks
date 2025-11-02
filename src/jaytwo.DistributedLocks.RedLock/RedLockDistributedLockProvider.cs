using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;

namespace jaytwo.DistributedLocks.RedLock;

public sealed class RedLockDistributedLockProvider : DistributedLockProvider, IDistributedLockProvider
{
    internal const string RedisProviderName = "Redis";
    internal const int DefaultLockWaitSecondsFallback = 30;

    internal static readonly TimeSpan DefaultExpiry = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan DefaultRetry = TimeSpan.FromMilliseconds(250); // faster retry usually better

    private readonly IDistributedLockFactory _redLockFactory;

    public RedLockDistributedLockProvider(IDistributedLockFactory redLockFactory, string lockNamespace, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        : base(RedisProviderName, lockNamespace, defaultLockWaitSeconds, logger)
    {
        if (redLockFactory == null)
        {
            throw new ArgumentNullException(nameof(redLockFactory));
        }

        _redLockFactory = redLockFactory;
    }

    public static RedLockDistributedLockProvider CreateWithDefaultLockNamespace(IDistributedLockFactory redLockFactory, ILogger? logger = default, int defaultLockWaitSeconds = DefaultLockWaitSecondsFallback)
        => new(redLockFactory, DefaultLockNamespace(logger), logger, defaultLockWaitSeconds);

    public override async Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = default, CancellationToken cancellationToken = default)
    {
        TimeSpan? waitTime = waitSeconds.HasValue ? TimeSpan.FromSeconds(waitSeconds.Value) : null;
        return await CreateLockAsync(resource, null, waitTime, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IDistributedLock> CreateLockAsync(string resource, TimeSpan? expiryTime, TimeSpan? waitTime, TimeSpan? retryTime, CancellationToken cancellationToken = default)
    {
        if (waitTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTime));
        }

        var lockAttemptId = Guid.NewGuid();
        var qualifiedResource = string.IsNullOrEmpty(LockNamespace) ? resource : $"{LockNamespace}:{resource}";
        var effectiveWaitTime = GetEffectiveWaitTime(waitTime);
        var effectiveExpiryTime = expiryTime ?? DefaultExpiry;
        var effectiveRetryTime = retryTime ?? DefaultRetry;

        var eventLogger = GetEventLogger(resource, qualifiedResource, lockAttemptId);
        using var loggerScope = eventLogger?.DefaultScope();

        eventLogger?.LogRequested(
            resource,
            requestWaitTime: waitTime,
            effectiveWaitTime: effectiveWaitTime,
            extraConfig: x => x.WithFields(
                ("expiry_time_seconds", eventLogger?.FormatTimeSeconds(effectiveExpiryTime)),
                ("retry_time_seconds", eventLogger?.FormatTimeSeconds(effectiveRetryTime))));

        return await CreateLockAsync(qualifiedResource, effectiveExpiryTime, effectiveWaitTime, effectiveRetryTime, eventLogger, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IDistributedLock> CreateLockAsync(string qualifiedResource, TimeSpan expiryTime, TimeSpan waitTime, TimeSpan retryTime, EventLogger? eventLogger, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(qualifiedResource))
        {
            throw new ArgumentException("Qualified Resource is required.", nameof(qualifiedResource));
        }

        if (expiryTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiryTime));
        }

        if (waitTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(waitTime));
        }

        if (retryTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(retryTime));
        }

        IRedLock? result = null;
        var acquired = false;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            result = await _redLockFactory.CreateLockAsync(
                resource: qualifiedResource,
                expiryTime: expiryTime,
                waitTime: waitTime,
                retryTime: retryTime,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            acquired = result != null && result.IsAcquired;
        }
        catch (OperationCanceledException ex)
        {
            stopwatch.Stop();
            eventLogger?.LogCancelled(stopwatch.Elapsed, ex);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            eventLogger?.LogError(stopwatch.Elapsed, ex);
            throw;
        }

        if (acquired)
        {
            eventLogger?.LogAcquired(stopwatch.Elapsed, x => x.WithFields(("redlock_lock_id", result?.LockId)));
        }
        else
        {
            eventLogger?.LogTimedOut(stopwatch.Elapsed);
        }

        if (acquired)
        {
            return new RedLockDistributedLock(this, result!, qualifiedResource, eventLogger);
        }
        else if (result != null)
        {
            await result.DisposeAsync();
        }

        return NullLock.Instance;
    }
}
