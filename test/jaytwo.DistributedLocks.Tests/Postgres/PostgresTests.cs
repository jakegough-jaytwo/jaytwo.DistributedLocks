using System;
using System.Threading.Tasks;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests.Postgres;

public class PostgresTests : IClassFixture<PostgresTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;
    private readonly IDistributedLockFactory _lockFactory;

    public PostgresTests(PostgresTestFixture fixture, ITestOutputHelper output)
    {
        _lockFactory = fixture.LockFactory;
        _connectionString = fixture.ConnectionString;
        _output = output;
    }

    [Fact]
    public void ConnectionStringHasDetails()
    {
        _output.WriteLine("Connection Sring: " + _connectionString);

        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(_connectionString!);

        Assert.NotNull(connectionStringBuilder.Host);
        Assert.NotNull(connectionStringBuilder.Database);
        Assert.NotNull(connectionStringBuilder.Username);
        Assert.NotNull(connectionStringBuilder.Password);
    }

    [Fact]
    public async Task CanConnect()
    {
        using var connection = new NpgsqlConnection(_connectionString);

        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CanAcquireLockAsync(int waitSeconds)
    {
        // arrange
        var key = Guid.NewGuid().ToString();

        // act
        using (var firstLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            // assert
            Assert.True(firstLock.IsAcquired);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DisposingLockReleasesKey(int waitSeconds)
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        bool firstLockAcquired;
        bool secondLockAcquired;

        // Act
        using (var firstLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            firstLockAcquired = firstLock.IsAcquired;
        }

        using (var secondtLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            secondLockAcquired = secondtLock.IsAcquired;
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.True(secondLockAcquired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task AcquiredLockBlocksAnotherLockAsync(int waitSeconds)
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        bool firstLockAcquired;
        bool secondLockAcquired;

        // Act
        using (var firstLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
        {
            firstLockAcquired = firstLock.IsAcquired;

            using (var secondLockWait = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(waitSeconds)))
            {
                secondLockAcquired = secondLockWait.IsAcquired;
            }
        }

        // Assert
        Assert.True(firstLockAcquired);
        Assert.False(secondLockAcquired);
    }
}
