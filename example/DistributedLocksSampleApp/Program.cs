using DistributedLocksSampleApp;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using jaytwo.DistributedLocks.SqlServer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Npgsql;
using RedLockNet;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

//builder.Logging.SetMinimumLevel(LogLevel.Debug);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
    });

// TODO: make logging automagic with DI... which probably means making DI'ing an options object and injecting that automagically into the provider
builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:PostgresDb"];
    var connectionFactory = () => new NpgsqlConnection(connectionString);
    var logger = x.GetRequiredService<ILogger<PostgresDistributedLockProvider>>();
    return PostgresDistributedLockProvider.CreateWithDefaultLockNamespace(connectionFactory, logger);
});

builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:MySqlDb"];
    var connectionFactory = () => new MySqlConnection(connectionString);
    var logger = x.GetRequiredService<ILogger<MySqlDistributedLockProvider>>();
    return MySqlDistributedLockProvider.CreateWithDefaultLockNamespace(connectionFactory, logger);
});

builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:SqlServerDb"];
    var connectionFactory = () => new SqlConnection(connectionString);
    var logger = x.GetRequiredService<ILogger<SqlServerDistributedLockProvider>>();
    return SqlServerDistributedLockProvider.CreateWithDefaultLockNamespace(connectionFactory, logger);
});

RedisSetup.ConfigureRedis(builder.Services, "sampleapp");
builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var redLockFactory = x.GetRequiredService<IDistributedLockFactory>();
    var logger = x.GetRequiredService<ILogger<RedLockDistributedLockProvider>>();
    return RedLockDistributedLockProvider.CreateWithDefaultLockNamespace(redLockFactory, logger);
});

var app = builder.Build();

app.MapControllers();

app.Run();
