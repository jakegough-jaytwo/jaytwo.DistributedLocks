using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace DistributedLocksSampleApp;

internal static class RedisSetup
{
    public static void ConfigureRedis(
        IServiceCollection services,
        string clientName)
    {
        services.AddTransient(x => CreateConfigurationOptions(x, clientName));
        services.AddSingleton(x => CreateConnectionMultiplexer(x));
        services.AddTransient(x => x.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

        // redlock
        services.AddSingleton(x => CreateRedLockFactory(x, clientName));
    }

    private static ConfigurationOptions CreateConfigurationOptions(IServiceProvider serviceProvider, string clientName)
    {
        var config = serviceProvider.GetRequiredService<IConfiguration>();
        var redisOptions = ConfigurationOptions.Parse(config.GetConnectionString("Redis"));
        redisOptions.AbortOnConnectFail = false;
        redisOptions.Password = config.GetConnectionString("RedisPassword");
        redisOptions.ClientName = clientName;

        return redisOptions;
    }

    private static IConnectionMultiplexer CreateConnectionMultiplexer(IServiceProvider serviceProvider)
    {
        var redisOptions = serviceProvider.GetRequiredService<ConfigurationOptions>();
        return ConnectionMultiplexer.Connect(redisOptions);
    }

    private static IDistributedLockFactory CreateRedLockFactory(IServiceProvider serviceProvider, string clientName)
    {
        var connectionMultiplexer = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        var redLockMuultiplexer = new RedLockMultiplexer(connectionMultiplexer)
        {
            RedisKeyFormat = ":redlock:{0}",
        };

        return RedLockFactory.Create(new List<RedLockMultiplexer> { redLockMuultiplexer });
    }
}
