using System.Threading.Tasks;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class PostgresTests : IClassFixture<TestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;

    public PostgresTests(TestFixture fixture, ITestOutputHelper output)
    {
        _connectionString = fixture.PostgresConnectionString;
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
}
