using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public class LogBuilder
{
    private readonly ILogger? _logger;
    private readonly string _message;
    private readonly List<object?> _messageArgs;
    private readonly Dictionary<string, object?> _fields = new();
    private readonly Dictionary<string, object?> _messageFields = new();

    private Func<EventId>? _eventIdDelegate = null;
    private Exception? _exception = null;

    public LogBuilder(ILogger? logger, string message, params object?[] messageArgs)
    {
        _logger = logger;
        _message = message;
        _messageArgs = new(messageArgs ?? Array.Empty<object?>());
    }

    public LogBuilder WithExtraConfig(Action<LogBuilder>? extraConfig)
    {
        extraConfig?.Invoke(this);
        return this;
    }

    public LogBuilder WithException(Exception? ex)
    {
        _exception = ex;
        return this;
    }

    public LogBuilder WithEventId(EventId eventId)
    {
        _eventIdDelegate = () => eventId;
        return this;
    }

    public LogBuilder WithEventIdFromString(string eventName)
    {
        _eventIdDelegate = () => new EventId(HashToInt(eventName), eventName);
        return this;
    }

    public LogBuilder WithField(string key, object? value)
    {
        _fields[key] = value;
        return this;
    }

    public LogBuilder WithFields(params (string Key, object? Value)[] items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder WithFields(params KeyValuePair<string, object?>[] items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder WithFields(IEnumerable<KeyValuePair<string, object?>> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder WithFields(IDictionary<string, object?> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder WithFields<T>(IEnumerable<KeyValuePair<string, T>> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder WithFields<T>(IDictionary<string, T> items)
    {
        foreach (var kv in items)
        {
            WithField(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage(string key, object? value)
    {
        _messageFields[key] = value;
        return this;
    }

    public LogBuilder AppendToMessage(params (string Key, object? Value)[] items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage(params KeyValuePair<string, object?>[] items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage(IEnumerable<KeyValuePair<string, object?>> items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage(IDictionary<string, object?> items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage<T>(IEnumerable<KeyValuePair<string, T>> items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public LogBuilder AppendToMessage<T>(IDictionary<string, T> items)
    {
        foreach (var kv in items)
        {
            AppendToMessage(kv.Key, kv.Value);
        }

        return this;
    }

    public void Write(LogLevel logLevel)
    {
        if (_logger is null || !_logger.IsEnabled(logLevel))
        {
            return;
        }

        using var scope = _fields.Count == 0 ? null : _logger.BeginScope(_fields.AsEnumerable());

        var final = BuildLog();
        var eventId = _eventIdDelegate?.Invoke() ?? default;

        _logger.Log(logLevel, eventId, _exception, final.Message, final.Args);
    }

    public void Critical() => Write(LogLevel.Critical);

    public void Error() => Write(LogLevel.Error);

    public void Warning() => Write(LogLevel.Warning);

    public void Information() => Write(LogLevel.Information);

    public void Debug() => Write(LogLevel.Debug);

    public void Trace() => Write(LogLevel.Trace);

    private static int HashToInt(string input)
    {
        using var md5 = MD5.Create();
        byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));

        // Make non-negative by clearing the sign bit
        return (int)(BitConverter.ToUInt32(hashBytes, 0) & 0x7FFF_FFFF);
    }

    private (string Message, object?[] Args) BuildLog()
    {
        var message = _message;
        var args = new List<object?>(_messageArgs);

        foreach (var kv in _messageFields)
        {
            message += $$""" [{{kv.Key}}]={{{kv.Key}}}""";
            args.Add(kv.Value);
        }

        return (message, args.ToArray());
    }
}
