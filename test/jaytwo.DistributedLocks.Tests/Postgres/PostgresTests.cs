using System;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using MySql.Data.MySqlClient;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests.Postgres;

public class PostgresTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PostgresTests(PostgresTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void ConnectionStringHasDetails()
    {
        var connectionString = _fixture.ConnectionString;
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString!);

        _output.WriteLine("Connection Sring: " + connectionString);

        Assert.NotNull(connectionStringBuilder.Host);
        Assert.NotNull(connectionStringBuilder.Database);
        Assert.NotNull(connectionStringBuilder.Username);
        Assert.NotNull(connectionStringBuilder.Password);
    }

    [Fact]
    public async Task CanConnect()
    {
        using var connection = _fixture.CreateConnection();

        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CanAcquireLockAsync()
    {
        // arrange
        var key = Guid.NewGuid().ToString();
        using var factory = _fixture.LockFactory;

        // act
        using (var firstLock = await factory.CreateLockAsync(key))
        {
            // assert
            Assert.True(firstLock.IsAcquired);
        }
    }

    [Fact]
    public async Task DisposingLockReleasesKey()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        using var factory = _fixture.LockFactory;

        using (var firstLock = await factory.CreateLockAsync(key))
        {
        }

        // Act
        using (var secondtLock = await factory.CreateLockAsync(key, TimeSpan.FromSeconds(2)))
        {
            // Assert
            Assert.True(secondtLock.IsAcquired);
        }
    }

    [Fact]
    public async Task AcquiredLockBlocksAnotherLockAsync()
    {
        // Arrange
        var key = Guid.NewGuid().ToString();
        using var factory = _fixture.LockFactory;

        using (var firstLock = await factory.CreateLockAsync(key))
        {
            // Act
            using (var secondLock = await factory.CreateLockAsync(key, TimeSpan.FromSeconds(1)))
            {
                // Assert
                Assert.True(firstLock.IsAcquired);
                Assert.False(secondLock.IsAcquired);
            }
        }
    }
}
