using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class CommonTests : IClassFixture<TestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly DistributedLockProviderFactory _lockProviders;

    public CommonTests(ITestOutputHelper output, TestFixture fixture)
    {
        _output = output;

        _lockProviders = new DistributedLockProviderFactory(
            fixture.MySqlConnectionString,
            fixture.PostgresConnectionString,
            fixture.RedisConnectionString);
    }

    [Theory]
    [InlineData(Monikers.InMemory, 0)]
    [InlineData(Monikers.InMemory, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
    [InlineData(Monikers.RedLock, 0)]
    [InlineData(Monikers.RedLock, 1)]
    public async Task CanAcquireLockAsync(string moniker, int waitSeconds)
    {
        // arrange
        var key = Guid.NewGuid().ToString();
        using var lockProvider = _lockProviders.GetProvider(moniker);

        // act
        using (var firstLock = await lockProvider.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            // assert
            Assert.True(firstLock.IsAcquired);
        }
    }

    [Theory]
    [InlineData(Monikers.InMemory, 0)]
    [InlineData(Monikers.InMemory, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
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
        using (var firstLock = await lockProvider.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            firstLockAcquired = firstLock.IsAcquired;
        }

        using (var secondtLock = await lockProvider.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            secondLockAcquired = secondtLock.IsAcquired;
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.True(secondLockAcquired);
    }

    [Theory]
    [InlineData(Monikers.InMemory, 0)]
    [InlineData(Monikers.InMemory, 1)]
    [InlineData(Monikers.MySql, 0)]
    [InlineData(Monikers.MySql, 1)]
    [InlineData(Monikers.Postgres, 0)]
    [InlineData(Monikers.Postgres, 1)]
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
        using (var firstLock = await lockProvider.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            firstLockAcquired = firstLock.IsAcquired;

            using (var secondLockWait = await lockProvider.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
            {
                secondLockAcquired = secondLockWait.IsAcquired;
            }
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.False(secondLockAcquired);
    }
}
