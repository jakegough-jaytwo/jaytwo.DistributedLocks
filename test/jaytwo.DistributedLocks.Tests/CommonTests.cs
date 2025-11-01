using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class CommonTests : IClassFixture<TestFixture>
{
    private readonly ILogger _logger;
    private readonly ITestOutputHelper _output;
    private readonly DistributedLockProviderFactory _lockProviders;

    public CommonTests(ITestOutputHelper output, TestFixture fixture)
    {
        _output = output;

        var loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Trace);

            b.AddXUnit(output, opt =>
            {
                opt.IncludeScopes = true;
                //opt.Format = XUnitFormatterNames.Systemd; // matches SimpleConsole-style
            });
        });

        _logger = loggerFactory.CreateLogger<CommonTests>();

        _lockProviders = new DistributedLockProviderFactory(
            _logger,
            fixture.MySqlConnectionString,
            fixture.PostgresConnectionString,
            fixture.SqlServerConnectionString,
            fixture.RedisConnectionString);
    }

    [Theory]
    [InlineData(Monikers.InProcess)]
    [InlineData(Monikers.MySql)]
    [InlineData(Monikers.Postgres)]
    [InlineData(Monikers.SqlServer)]
    [InlineData(Monikers.RedLock)]
    public async Task HealthCheckAsync(string moniker)
    {
        // arrange
        using var lockProvider = _lockProviders.GetProvider(moniker);

        // act
        var healthCheckResult = await lockProvider.HealthCheckAsync();

        // assert
        Assert.NotNull(healthCheckResult);

        _output.WriteLine(string.Empty);
        _output.WriteLine("Healthcheck:");
        _output.WriteLine(JsonSerializer.Serialize(healthCheckResult, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Theory]
    [InlineData(Monikers.InProcess, 0)]
    [InlineData(Monikers.InProcess, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
    [InlineData(Monikers.SqlServer, 0)]
    [InlineData(Monikers.SqlServer, 1)]
    [InlineData(Monikers.RedLock, 0)]
    [InlineData(Monikers.RedLock, 1)]
    public async Task CanAcquireLockAsync(string moniker, int waitSeconds)
    {
        // arrange
        var key = Guid.NewGuid().ToString();
        using var lockProvider = _lockProviders.GetProvider(moniker);

        // act
        using (var firstLock = await lockProvider.CreateLockAsync(key, waitSeconds))
        {
            // assert
            Assert.True(firstLock.IsAcquired);
        }
    }

    [Theory]
    [InlineData(Monikers.InProcess, 0)]
    [InlineData(Monikers.InProcess, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
    [InlineData(Monikers.SqlServer, 0)]
    [InlineData(Monikers.SqlServer, 1)]
    [InlineData(Monikers.RedLock, 0)]
    [InlineData(Monikers.RedLock, 1)]
    public async Task DisposingLockReleasesKey(string moniker, int waitSeconds)
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        using var lockProvider = _lockProviders.GetProvider(moniker);
        bool firstLockAcquired;
        bool secondLockAcquired;

        // Act
        using (var firstLock = await lockProvider.CreateLockAsync(key, waitSeconds))
        {
            firstLockAcquired = firstLock.IsAcquired;
        }

        using (var secondtLock = await lockProvider.CreateLockAsync(key, waitSeconds))
        {
            secondLockAcquired = secondtLock.IsAcquired;
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.True(secondLockAcquired);
    }

    [Theory]
    [InlineData(Monikers.InProcess, 0)]
    [InlineData(Monikers.InProcess, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
    [InlineData(Monikers.SqlServer, 0)]
    [InlineData(Monikers.SqlServer, 1)]
    [InlineData(Monikers.RedLock, 0)]
    [InlineData(Monikers.RedLock, 1)]
    public async Task AcquiredLockBlocksAnotherLockAsync(string moniker, int waitSeconds)
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        using var lockProvider = _lockProviders.GetProvider(moniker);
        bool firstLockAcquired;
        bool secondLockAcquired;

        // Act
        using (var firstLock = await lockProvider.CreateLockAsync(key, waitSeconds))
        {
            firstLockAcquired = firstLock.IsAcquired;

            using (var secondLockWait = await lockProvider.CreateLockAsync(key, waitSeconds))
            {
                secondLockAcquired = secondLockWait.IsAcquired;
            }
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.False(secondLockAcquired);
    }
}
