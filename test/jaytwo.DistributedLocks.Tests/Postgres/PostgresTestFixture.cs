using System.IO;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Npgsql;

namespace jaytwo.DistributedLocks.Tests.Postgres;

public class PostgresTestFixture
{
    public PostgresTestFixture()
    {
        var assmeblyLocation = GetType().Assembly.Location;
        var basePath = new FileInfo(assmeblyLocation!).Directory!.FullName;

        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("testsettings.json")
            .Build();

        ConnectionString = Configuration.GetConnectionString("PostgresDb")!;
        LockFactory = new PostgresDistributedLockFactory(ConnectionString);
    }

    public IConfiguration Configuration { get; }

    public string ConnectionString { get; }

    public PostgresDistributedLockFactory LockFactory { get; }

    public NpgsqlConnection CreateConnection()
        => new NpgsqlConnection(ConnectionString);
}
