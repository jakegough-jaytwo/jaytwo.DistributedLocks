using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public static class LoggerExtensions
{
    public static LogBuilder BuildMessage(this ILogger? logger, string message)
        => BuildMessage(logger, message, Array.Empty<object?>());

    public static LogBuilder BuildMessage(this ILogger? logger, string message, params object?[] messageArgs)
        => new LogBuilder(logger, message, messageArgs);

    public static ScopeBuilder BuildScope(this ILogger? logger)
        => new ScopeBuilder(logger);

    public static IDisposable? BeginScope(this ILogger? logger, params (string Key, object? Value)[] values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value));

    public static IDisposable? BeginScope(this ILogger? logger, params IEnumerable<KeyValuePair<string, object?>> values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value));

    public static IDisposable? BeginScope(this ILogger? logger, params KeyValuePair<string, object?>[] values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value));

    public static IDisposable? BeginScope<T>(this ILogger? logger, params IEnumerable<KeyValuePair<string, T>> values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value));

    public static IDisposable? BeginScope<T>(this ILogger? logger, params KeyValuePair<string, T>[] values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value));

    public static IDisposable? BeginScope<T>(this ILogger? logger, IDictionary<string, T> values)
        => BeginScope(logger, values.ToDictionary(x => x.Key, x => x.Value as object));

    public static IDisposable? BeginScope(this ILogger? logger, IDictionary<string, object?> values)
        => logger?.BeginScope<IDictionary<string, object?>>(values);
}
