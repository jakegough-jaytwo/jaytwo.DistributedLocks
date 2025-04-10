using System.IO;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using Microsoft.Extensions.Configuration;

namespace jaytwo.DistributedLocks.Tests;

public class TestFixture
{
    public TestFixture()
    {
        var assmeblyLocation = GetType().Assembly.Location;
        var basePath = new FileInfo(assmeblyLocation!).Directory!.FullName;

        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("testsettings.json")
            .Build();

        PostgresConnectionString = Configuration.GetConnectionString("PostgresDb")!;

        MySqlConnectionString = Configuration.GetConnectionString("MySqlDb")!;

        RedisConnectionString = Configuration.GetConnectionString("Redis")!;
    }

    public IConfiguration Configuration { get; }

    public string PostgresConnectionString { get; }

    public string MySqlConnectionString { get; }

    public string RedisConnectionString { get; }
}
