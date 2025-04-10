using System.IO;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Npgsql;

namespace jaytwo.DistributedLocks.Tests.MySql;

public class MySqlTestFixture
{
    public MySqlTestFixture()
    {
        var assmeblyLocation = GetType().Assembly.Location;
        var basePath = new FileInfo(assmeblyLocation!).Directory!.FullName;

        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("testsettings.json")
            .Build();

        ConnectionString = Configuration.GetConnectionString("MySqlDb")!;
        LockFactory = new MySqlDistributedLockFactory(ConnectionString);
    }

    public IConfiguration Configuration { get; }

    public string ConnectionString { get; }

    public MySqlDistributedLockFactory LockFactory { get; }

    public MySqlConnection CreateConnection()
        => new MySqlConnection(ConnectionString);
}
