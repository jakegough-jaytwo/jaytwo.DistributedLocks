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
    public static IDistributedLockFactory CreateRedLockDistributedLockFactory(string connectionString)
    {
        var redisOptions = ConfigurationOptions.Parse(connectionString);
        var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
        var redLockMuultiplexer = new RedLockMultiplexer(connectionMultiplexer);
        return RedLockFactory.Create(new List<RedLockMultiplexer> { redLockMuultiplexer });
    }
}
