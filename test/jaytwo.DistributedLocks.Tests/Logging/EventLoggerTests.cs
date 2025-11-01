using System;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests.Logging;

public class EventLoggerTests
{
    private readonly ITestOutputHelper _output;

    public EventLoggerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static EventLogger CreateEventLogger(
        ILogger? logger = default,
        string? providerName = default,
        string? lockNamespace = default,
        string? resource = default,
        string? providerResource = default,
        Guid? lockAttemptId = default)
        => new EventLogger(
            logger,
            providerName: providerName ?? "noProvider",
            lockNamespace: lockNamespace ?? "noNamespace",
            resource: resource ?? "noResource",
            providerResource: resource ?? "noProviderResource",
            lockAttemptId: lockAttemptId ?? Guid.Empty);

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, "0")]
    [InlineData(1, "0.0000001")]
    public void EventLogger_FormatTimeSeconds_nullable(long? ticks, string expected)
    {
        // arrange
        TimeSpan? ts = ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : null;
        var sut = CreateEventLogger();

        // act
        var actual = sut.FormatTimeSeconds(ts);
        _output.WriteLine(actual ?? string.Empty);

        // assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1, "0.0000001")]
    [InlineData(0, "0")]
    [InlineData(-1, "-0.0000001")]
    [InlineData(long.MaxValue, "922337203685.4775807")]
    public void EventLogger_FormatTimeSeconds(long ticks, string expected)
    {
        // arrange
        TimeSpan ts = TimeSpan.FromTicks(ticks);
        var sut = CreateEventLogger();

        // act
        var actual = sut.FormatTimeSeconds(ts);
        _output.WriteLine(actual);

        // assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, "0s")]
    [InlineData(1, "100ns")]
    public void EventLogger_FormatTimePretty_nullable(long? ticks, string expected)
    {
        // arrange
        TimeSpan? ts = ticks.HasValue ? TimeSpan.FromTicks(ticks.Value) : null;
        var sut = CreateEventLogger();

        // act
        var actual = sut.FormatTimePretty(ts);
        _output.WriteLine(actual ?? string.Empty);

        // assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(1, "100ns")]
    [InlineData(0, "0s")]
    [InlineData(-1, "-100ns")]
    [InlineData(long.MaxValue, "256204778h48m5s")]
    public void EventLogger_FormatTimePretty(long ticks, string expected)
    {
        // arrange
        TimeSpan ts = TimeSpan.FromTicks(ticks);
        var sut = CreateEventLogger();

        // act
        var actual = sut.FormatTimePretty(ts);
        _output.WriteLine(actual);

        // assert
        Assert.Equal(expected, actual);
    }
}
