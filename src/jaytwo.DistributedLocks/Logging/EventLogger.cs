using System;
using jaytwo.TimeExpression.Golang;
using jaytwo.TimeExpression.Seconds;
using Microsoft.Extensions.Logging;

namespace jaytwo.DistributedLocks.Logging;

public abstract class EventLogger
{
    private readonly ILogger? _logger;

    public EventLogger(ILogger? logger)
    {
        _logger = logger;
    }

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
}
