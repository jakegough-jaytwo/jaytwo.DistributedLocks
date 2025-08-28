using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace jaytwo.DistributedLocks.Tests;

public class TestFixture
{
    public TestFixture()
    {
        var assmeblyLocation = GetType().Assembly.Location;
        var basePath = new FileInfo(assmeblyLocation!).Directory!.FullName;

        TestEnvironment = Environment.GetEnvironmentVariable("TEST_ENV");

        Configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("testsettings.json")
            .AddJsonFile($"testsettings.{TestEnvironment}.json", optional: true)
            .Build();

        PostgresConnectionString = Configuration.GetConnectionString("PostgresDb")!;

        MySqlConnectionString = Configuration.GetConnectionString("MySqlDb")!;

        SqlServerConnectionString = Configuration.GetConnectionString("SqlServerDb")!;

        RedisConnectionString = Configuration.GetConnectionString("Redis")!;
    }

    public IConfiguration Configuration { get; }

    public string? TestEnvironment { get; }

    public string PostgresConnectionString { get; }

    public string MySqlConnectionString { get; }

    public string SqlServerConnectionString { get; }

    public string RedisConnectionString { get; }
}
