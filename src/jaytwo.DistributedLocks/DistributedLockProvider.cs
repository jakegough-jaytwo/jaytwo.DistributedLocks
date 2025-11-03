using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks;

public abstract class DistributedLockProvider : IDistributedLockProvider
{
    protected DistributedLockProvider(string providerName, string lockNamespace, int defaultLockWaitSeconds, ILogger? logger)
    {
        if (string.IsNullOrEmpty(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        if (string.IsNullOrEmpty(lockNamespace))
        {
            throw new ArgumentException("Lock namespace is required.", nameof(lockNamespace));
        }

        if (defaultLockWaitSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(defaultLockWaitSeconds), "Must be >= 0.");
        }

        ProviderName = providerName;
        LockNamespace = lockNamespace;
        DefaultLockWaitSeconds = defaultLockWaitSeconds;
        Logger = logger;
    }

    public string ProviderName { get; }

    public string LockNamespace { get; }

    public int DefaultLockWaitSeconds { get; }

    protected ILogger? Logger { get; }

    public abstract Task<IDistributedLock> CreateLockAsync(string resource, int? waitSeconds = null, CancellationToken cancellationToken = default);

    public virtual void Dispose()
    {
    }

    public virtual ValueTask DisposeAsync()
    {
        return default;
    }

    public virtual async Task<IReadOnlyDictionary<string, object>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, object>();

        result["settings"] = new
        {
            LockNamespace,
            DefaultLockWaitSeconds,
        };

        result["test_lock"] = await HealthCheckTestLockAsync(cancellationToken);

        return result;
    }

    internal static bool TryGetEnvironmentVariable(string variable, out string? value)
    {
        value = Environment.GetEnvironmentVariable(variable)?.Trim();
        return !string.IsNullOrEmpty(value);
    }

    internal static TimeSpan GetEffectiveWaitTime(TimeSpan? timeout, int defaultTimeoutSeconds)
        => timeout ?? TimeSpan.FromSeconds(defaultTimeoutSeconds);

    internal static int GetEffectiveWaitMs(TimeSpan? timeout, int defaultTimeoutSeconds)
        => (int)Math.Ceiling(GetEffectiveWaitTime(timeout, defaultTimeoutSeconds).TotalMilliseconds);

    internal static string DefaultLockNamespace(ProviderEventLogger? eventLogger)
    {
        var applicationName = Assembly.GetEntryAssembly()?.GetName().Name ?? AppDomain.CurrentDomain.FriendlyName;

        if (TryGetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", out var environmentName) || TryGetEnvironmentVariable("DOTNET_ENVIRONMENT", out environmentName))
        {
            var lockNamespace = $"{applicationName}:{environmentName}";
            eventLogger?.LogUsingDefaultNamespace(lockNamespace, applicationName, environmentName!);
            return lockNamespace;
        }
        else
        {
            eventLogger?.LogUsingDefaultNamespaceWithoutEnvironmentName(applicationName, applicationName);
            return applicationName;
        }
    }

    protected internal static string DefaultLockNamespace(ILogger? logger, string providerName)
        => DefaultLockNamespace(new ProviderEventLogger(logger, providerName));

    protected internal static long HashStringToLong(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt64(hashBytes, 0);
    }

    protected internal static string HashStringToHex(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToString(hashBytes).Replace("-", string.Empty).ToLowerInvariant();
    }

    protected virtual async Task<object> HealthCheckTestLockAsync(CancellationToken cancellationToken)
    {
        var resource = "hc_" + Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        await using var myLock = await CreateLockAsync(resource, waitSeconds: 0, cancellationToken);
        stopwatch.Stop();

        return new
        {
            resource,
            acquired = myLock.IsAcquired,
            elapsed_ms = stopwatch.ElapsedMilliseconds,
        };
    }

    protected LockEventLogger? GetEventLogger(string resource, object providerResource, Guid lockAttemptId)
        => Logger != null ? new LockEventLogger(Logger, this, resource, providerResource, lockAttemptId) : null;

    protected TimeSpan GetEffectiveWaitTime(TimeSpan? waitTime)
        => GetEffectiveWaitTime(waitTime, DefaultLockWaitSeconds);

    protected int GetEffectiveWaitMs(TimeSpan? waitTime)
        => GetEffectiveWaitMs(waitTime, DefaultLockWaitSeconds);
}
