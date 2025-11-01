using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using jaytwo.TimeExpression.Golang;
using jaytwo.TimeExpression.Seconds;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public class EventLogger
{
    private readonly ILogger? _logger;

    public EventLogger(ILogger? logger, DistributedLockProvider provider, string resource, object providerResource, Guid lockAttemptId)
        : this(logger, provider.ProviderName, provider.LockNamespace, resource, providerResource, lockAttemptId)
    {
    }

    public EventLogger(ILogger? logger, string providerName, string lockNamespace, string resource, object providerResource, Guid lockAttemptId)
    {
        _logger = logger;
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

    public IDisposable? Scope(Action<ScopeBuilder>? extraConfig = null)
    {
        if (_logger == null)
        {
            return null;
        }

        var builder = _logger.BuildScope();

        extraConfig?.Invoke(builder);

        return builder.BuildScope();
    }

    public IDisposable? DefaultScope(Action<ScopeBuilder>? extraConfig = null)
    {
        if (_logger == null)
        {
            return null;
        }

        var builder = _logger.BuildScope();

        builder.WithFields(
            ("resource", Resource),
            ("lock_attempt_id", LockAttemptId),
            ("lock_namespace", LockNamespace),
            ("provider", ProviderName));

        extraConfig?.Invoke(builder);

        return builder.BuildScope();
    }

    public void LogRequested(object requestResource, TimeSpan? requestWaitTime, TimeSpan effectiveWaitTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.Requested,
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
                EventIds.TimedOut,
                "Lock not acquired after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(extraConfig)
            .Information();

    public void LogCancelled(TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.Cancelled,
                "Acquire lock cancelled after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

    public void LogError(TimeSpan elapsedTime, Exception ex, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.Error,
                "Acquire lock failed after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

    public void LogAcquired(TimeSpan elapsedTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.Acquired,
                "Lock acquired successfully after {elapsed_time} for resource {provider_resource}.",
                FormatTimePretty(elapsedTime),
                ProviderResource)
            ?.WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithExtraConfig(extraConfig)
            .Information();

    public void LogReleased(TimeSpan elapsedTime, TimeSpan lockHeldTime, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.Released,
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
                EventIds.ReleaseFailed,
                "Lock released failed for resource {provider_resource}.",
                ProviderResource)
            ?.AppendToMessage(("elapsed_time", FormatTimePretty(elapsedTime)))
            .WithFields(("elapsed_time_seconds", FormatTimeSeconds(elapsedTime)))
            .WithException(ex)
            .WithExtraConfig(extraConfig)
            .Error();

    public virtual string FormatTimePretty(TimeSpan duration) => GoDuration.ToCompactString(duration);

    public virtual string? FormatTimePretty(TimeSpan? duration) => GoDuration.ToCompactString(duration);

    public virtual string FormatTimeSeconds(TimeSpan duration) => ExactSeconds.ToString(duration);

    public virtual string? FormatTimeSeconds(TimeSpan? duration) => ExactSeconds.ToString(duration);

    protected internal virtual LogBuilder? BuildMessage(EventId eventId, string message, params object?[] args)
        => _logger?.BuildMessage(
                "[{event_name}] " + message,
                PrependArray(eventId.Name, args))
            .WithEventId(eventId);

    private static object?[] PrependArray(object? first, object?[] args)
    {
        if (args.Length == 0)
        {
            return new object?[] { first };
        }

        var arr = new object?[args.Length + 1];
        arr[0] = first;
        Array.Copy(args, 0, arr, 1, args.Length);
        return arr;
    }

    public static class EventIds
    {
        public static readonly EventId Requested = EventIdFromString(Prefix + "REQUESTED");

        public static readonly EventId Acquired = EventIdFromString(Prefix + "ACQUIRED");

        public static readonly EventId Error = EventIdFromString(Prefix + "ERROR");

        public static readonly EventId TimedOut = EventIdFromString(Prefix + "TIMED_OUT");

        public static readonly EventId Cancelled = EventIdFromString(Prefix + "CANCELLED");

        public static readonly EventId Released = EventIdFromString(Prefix + "RELEASED");

        public static readonly EventId ReleaseFailed = EventIdFromString(Prefix + "RELEASE_FAILED");

        private const string Prefix = "DISTRIBUTED_LOCK:";

        private static EventId EventIdFromString(string eventName)
            => new EventId(HashToInt(eventName), eventName);

        private static int HashToInt(string input)
        {
            using var md5 = MD5.Create();
            byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

            // Make non-negative by clearing the sign bit
            return (int)(BitConverter.ToUInt32(hashBytes, 0) & 0x7FFF_FFFF);
        }
    }
}
