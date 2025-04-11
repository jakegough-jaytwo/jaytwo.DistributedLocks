using MySql.Data.MySqlClient;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class MySqlTests : IClassFixture<TestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;

    public MySqlTests(TestFixture fixture, ITestOutputHelper output)
    {
        _connectionString = fixture.MySqlConnectionString;
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
}
