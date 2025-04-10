using DistributedLocksSampleApp;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using Npgsql;
using RedLockNet.SERedis.Events;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:PostgresDb"];
    var connectionFactory = () => new NpgsqlConnection(connectionString);
    return new PostgresDistributedLockFactory("foo", connectionFactory);
});

builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:MySqlDb"];
    var connectionFactory = () => new MySqlConnection(connectionString);
    return new MySqlDistributedLockFactory("bar", connectionFactory);
});

RedisSetup.ConfigureRedis(builder.Services, "sampleapp");
builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var redLockFactory = x.GetRequiredService<global::RedLockNet.IDistributedLockFactory>();
    return new RedLockDistributedLockFactory("fizz", redLockFactory);
});

var app = builder.Build();

app.MapControllers();

app.Run();
