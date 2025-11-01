using System;
using jaytwo.DistributedLocks.Logging;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests.Logging;

public class LogBuilderTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public LogBuilderTests(ITestOutputHelper output)
    {
        _output = output;

        _loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);

            b.AddXUnit(output, opt =>
            {
                opt.IncludeScopes = true;
                //opt.Format = XUnitFormatterNames.Systemd; // matches SimpleConsole-style
            });
        });

        _logger = _loggerFactory.CreateLogger<LogBuilderTests>();
    }

    [Fact]
    public void BuildMessageWithField()
    {
        _logger.BuildMessage("hello {world}", "earth")
            .WithField("answer", 42)
            .Information();
    }
}
