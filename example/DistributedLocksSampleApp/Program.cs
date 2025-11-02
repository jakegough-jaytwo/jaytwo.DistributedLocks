using System;
using System.Data.Common;
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
using MySqlConnector;
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

// Postgres DI example
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresDb")
        ?? throw new InvalidOperationException("Missing PostgresDb connection string.")));

builder.Services.AddSingleton(x =>
{
    var dataSource = x.GetRequiredService<NpgsqlDataSource>();
    var logger = x.GetService<ILogger<PostgresDistributedLockProvider>>();
    return PostgresDistributedLockProvider.CreateWithDefaultLockNamespace(dataSource, logger);
});

// MySql DI example
builder.Services.AddSingleton(sp => new MySqlDataSource(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("MySqlDb")
        ?? throw new InvalidOperationException("Missing MySqlDb connection string.")));

builder.Services.AddSingleton(x =>
{
    var dataSource = x.GetRequiredService<MySqlDataSource>();
    var logger = x.GetService<ILogger<MySqlDistributedLockProvider>>();
    return MySqlDistributedLockProvider.CreateWithDefaultLockNamespace(dataSource, logger);
});

// SqlServer DI example
builder.Services.AddKeyedSingleton("SqlServerDb", (sp, _) => SqlClientFactory.Instance.CreateDataSource(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("SqlServerDb")
        ?? throw new InvalidOperationException("Missing SqlServerDb connection string.")));

builder.Services.AddSingleton(x =>
{
    var dataSource = x.GetRequiredKeyedService<DbDataSource>("SqlServerDb");
    var logger = x.GetService<ILogger<SqlServerDistributedLockProvider>>();
    return SqlServerDistributedLockProvider.CreateWithDefaultLockNamespace(dataSource, logger);
});

// Redis DI example
RedisSetup.ConfigureRedis(builder.Services, "sampleapp");
builder.Services.AddScoped(x =>
{
    var redLockFactory = x.GetRequiredService<IDistributedLockFactory>();
    var logger = x.GetService<ILogger<RedLockDistributedLockProvider>>();
    return RedLockDistributedLockProvider.CreateWithDefaultLockNamespace(redLockFactory, logger);
});

// resume the app bulid
var app = builder.Build();

app.MapControllers();

app.Run();
