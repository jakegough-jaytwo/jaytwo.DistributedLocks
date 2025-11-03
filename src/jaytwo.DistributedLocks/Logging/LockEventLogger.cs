using System;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public class LockEventLogger : EventLogger
{
    public LockEventLogger(ILogger? logger, DistributedLockProvider provider, string resource, object providerResource, Guid lockAttemptId)
        : this(logger, provider.ProviderName, provider.LockNamespace, resource, providerResource, lockAttemptId)
    {
    }

    public LockEventLogger(ILogger? logger, string providerName, string lockNamespace, string resource, object providerResource, Guid lockAttemptId)
        : base(logger)
    {
        ProviderName = providerName;
        LockNamespace = lockNamespace;
        Resource = resource;
        ProviderResource = providerResource;
        LockAttemptId = lockAttemptId;
    }

    public string ProviderName { get; }

    public string LockNamespace { get; }

    public string Resource { get; }

    public object ProviderResource { get; }

    public Guid LockAttemptId { get; }

    public IDisposable? DefaultScope(Action<ScopeBuilder>? extraConfig = null)
        => Scope(builder =>
        {
            builder.WithFields(
                ("resource", Resource),
                ("lock_attempt_id", LockAttemptId),
                ("lock_namespace", LockNamespace),
                ("provider", ProviderName));

            extraConfig?.Invoke(builder);
        });

    public void LogRequested(object requestResource, TimeSpan? requestWaitTime, TimeSpan effectiveWaitTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.Requested,
                "Acquire lock requested for resource: {provider_resource}.  Waiting up to {wait_time}.",
                ProviderResource,
                FormatTimePretty(effectiveWaitTime))
            ?.AppendToMessage(
                ("request_wait_time", FormatTimePretty(requestWaitTime)),
                ("request_resource", requestResource))
            .WithFields(
                ("request_wait_time_seconds", FormatTimeSeconds(requestWaitTime)),
                ("wait_time_seconds", FormatTimeSeconds(effectiveWaitTime)))
            .WithExtraConfig(extraConfig)
            .Debug();

    public void LogTimedOut(TimeSpan elapsedTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.TimedOut,
                "Lock not acquired after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(extraConfig)
            .Information();

    public void LogCancelled(TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.Cancelled,
                "Acquire lock cancelled after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

    public void LogError(TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.Error,
                "Acquire lock failed after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

    public void LogAcquired(TimeSpan elapsedTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.Acquired,
                "Lock acquired successfully after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(extraConfig)
            .Information();

    public void LogReleased(TimeSpan elapsedTime, TimeSpan lockHeldTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.Released,
                "Lock released for resource {provider_resource} (held for {lock_held_time}).",
                ProviderResource,
                FormatTimePretty(lockHeldTime))
            ?.AppendToMessage(("elapsed_time", FormatTimePretty(elapsedTime)))
            .WithFields(
                ("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)),
                ("lock_held_time_seconds", FormatTimeSeconds(lockHeldTime)))
            .WithExtraConfig(extraConfig)
            .Information(); // same log level as acquire success

    public void LogReleaseFailed(TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.LockEvents.ReleaseFailed,
                "Lock released failed for resource {provider_resource}.",
                ProviderResource)
            ?.AppendToMessage(("elapsed_time", FormatTimePretty(elapsedTime)))
            .WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();
}
