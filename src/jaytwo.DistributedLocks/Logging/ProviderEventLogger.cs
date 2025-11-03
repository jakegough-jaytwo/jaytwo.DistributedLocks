using System;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public class ProviderEventLogger : EventLogger
{
    public ProviderEventLogger(ILogger? logger, string providerName)
        : base(logger)
    {
        ProviderName = providerName;
    }

    public string ProviderName { get; }

    public IDisposable? DefaultScope(Action<ScopeBuilder>? extraConfig = null)
        => Scope(builder =>
        {
            builder.WithFields(
                ("provider", ProviderName));

            extraConfig?.Invoke(builder);
        });

    public void LogUsingDefaultNamespace(string lockNamespace, string applicationName, string environmentName, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.ProviderEvents.DefaultNamespaceApplied,
                "Using default lock namespace: {lock_namespace}.",
                lockNamespace)
            ?.AppendToMessage(
                ("provider", ProviderName))
            .WithFields(
                ("application_name", applicationName),
                ("environment_name", environmentName))
            .WithExtraConfig(extraConfig)
            .Information();

    public void LogUsingDefaultNamespaceWithoutEnvironmentName(string lockNamespace, string applicationName, Action<LogBuilder>? extraConfig = null)
        => BuildMessage(
                EventIds.ProviderEvents.DefaultNamespaceAppliedWithoutEnvironment,
                "Using default lock namespace without environment name: {lock_namespace}.  Collisions may occur if multiple environments share the same backend.",
                lockNamespace)
            ?.AppendToMessage(
                ("provider", ProviderName))
            .WithFields(
                ("application_name", applicationName))
            .WithExtraConfig(extraConfig)
            .Warning();
}
