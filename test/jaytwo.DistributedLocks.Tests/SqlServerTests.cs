using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace jaytwo.DistributedLocks.Tests;

public class SqlServerTests : IClassFixture<TestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;

    public SqlServerTests(TestFixture fixture, ITestOutputHelper output)
    {
        _connectionString = fixture.SqlServerConnectionString;
        _output = output;
    }

    [Fact]
    public void ConnectionStringHasDetails()
    {
        _output.WriteLine("Connection Sring: " + _connectionString);

        var connectionStringBuilder = new SqlConnectionStringBuilder(_connectionString);
        Assert.NotNull(connectionStringBuilder.DataSource);
        Assert.NotNull(connectionStringBuilder.InitialCatalog);
        Assert.NotNull(connectionStringBuilder.UserID);
        Assert.NotNull(connectionStringBuilder.Password);
    }

    [Fact]
    public async Task CanConnect()
    {
        using var connection = new SqlConnection(_connectionString);

        await connection.OpenAsync();

        Assert.Equal(System.Data.ConnectionState.Open, connection.State);
    }
}
