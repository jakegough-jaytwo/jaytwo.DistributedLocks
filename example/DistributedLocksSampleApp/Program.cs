using DistributedLocksSampleApp;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using MySql.Data.MySqlClient;
using Npgsql;
using RedLockNet;

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
    return new PostgresDistributedLockProvider("foo", connectionFactory);
});

builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var connectionString = config["ConnectionStrings:MySqlDb"];
    var connectionFactory = () => new MySqlConnection(connectionString);
    return new MySqlDistributedLockProvider("bar", connectionFactory);
});

RedisSetup.ConfigureRedis(builder.Services, "sampleapp");
builder.Services.AddScoped(x =>
{
    var config = x.GetRequiredService<IConfiguration>();
    var redLockFactory = x.GetRequiredService<IDistributedLockFactory>();
    return new RedLockDistributedLockProvider("fizz", redLockFactory);
});

var app = builder.Build();

app.MapControllers();

app.Run();
