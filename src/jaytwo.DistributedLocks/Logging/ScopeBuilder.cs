using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public class ScopeBuilder
{
    private readonly ILogger? _logger;
    private readonly Dictionary<string, object?> _fields = new();

    public ScopeBuilder(ILogger? logger)
    {
        _logger = logger;
    }

    public ScopeBuilder WithField(string key, object? value)
    {
        _fields[key] = value;
        return this;
    }

    public ScopeBuilder WithFields(params (string Key, object? Value)[] items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public ScopeBuilder WithFields(params KeyValuePair<string, object?>[] items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public ScopeBuilder WithFields(IEnumerable<KeyValuePair<string, object?>> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public ScopeBuilder WithFields(IDictionary<string, object?> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public ScopeBuilder WithFields<T>(IEnumerable<KeyValuePair<string, T>> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public ScopeBuilder WithFields<T>(IDictionary<string, T> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public IDisposable? BuildScope()
    {
        if (_logger is null || _fields.Count == 0)
        {
            return null;
        }

        return _logger.BeginScope(_fields.AsEnumerable());
    }
}
