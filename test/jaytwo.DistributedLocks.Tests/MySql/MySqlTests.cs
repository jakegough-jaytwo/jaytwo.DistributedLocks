using System;
using System.Threading.Tasks;
using jaytwo.DistributedLocks.MySql;
using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests.MySql;

public class MySqlTests : IClassFixture<MySqlTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;
    private readonly IDistributedLockFactory _lockFactory;

    public MySqlTests(MySqlTestFixture fixture, ITestOutputHelper output)
    {
        _lockFactory = fixture.LockFactory;
        _connectionString = fixture.ConnectionString;
        _output = output;
    }

    [Fact]
    public void ConnectionStringHasDetails()
    {
        _output.WriteLine("Connection Sring: " + _connectionString);

        var connectionStringBuilder = new MySqlConnectionStringBuilder(_connectionString);
        Assert.NotNull(connectionStringBuilder.Server);
        Assert.NotNull(connectionStringBuilder.Database);
        Assert.NotNull(connectionStringBuilder.UserID);
        Assert.NotNull(connectionStringBuilder.Password);
    }

    [Fact]
    public async Task CanConnect()
    {
        using var connection = new MySqlConnection(_connectionString);

        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }

    [Fact]
    public async Task CanAcquireLockAsync()
    {
        // arrange
        var key = Guid.NewGuid().ToString();

        // act
        using (var firstLock = await _lockFactory.CreateLockAsync(key))
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

        using (var firstLock = await _lockFactory.CreateLockAsync(key))
        {
        }

        // Act
        using (var secondtLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(2)))
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

        using (var firstLock = await _lockFactory.CreateLockAsync(key))
        {
            // Act
            using (var secondLock = await _lockFactory.CreateLockAsync(key, TimeSpan.FromSeconds(1)))
            {
                // Assert
                Assert.True(firstLock.IsAcquired);
                Assert.False(secondLock.IsAcquired);
            }
        }
    }
}
