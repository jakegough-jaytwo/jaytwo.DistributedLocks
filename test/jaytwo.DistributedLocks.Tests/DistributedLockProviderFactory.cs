using System;
using System.Collections.Generic;
using jaytwo.DistributedLocks.InProcess;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using jaytwo.DistributedLocks.SqlServer;
using Microsoft.Extensions.Logging;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace jaytwo.DistributedLocks.Tests;

public class DistributedLockProviderFactory
{
    private readonly Func<IDistributedLockProvider> _mySqlLockProvider;
    private readonly Func<IDistributedLockProvider> _postgresLockProvider;
    private readonly Func<IDistributedLockProvider> _sqlServerLockProvider;
    private readonly Func<IDistributedLockProvider> _redisLockProvider;
    private readonly Func<IDistributedLockProvider> _inProcessLockProvider;

    public DistributedLockProviderFactory(
        ILogger logger,
        string mySqlConnectionString,
        string postgresConnectionString,
        string sqlServerConnectionString,
        string redisConnectionString)
    {
        _mySqlLockProvider = () => MySqlDistributedLockProvider.CreateWithDefaultLockNamespace(mySqlConnectionString, logger);
        _postgresLockProvider = () => PostgresDistributedLockProvider.CreateWithDefaultLockNamespace(postgresConnectionString, logger);
        _sqlServerLockProvider = () => SqlServerDistributedLockProvider.CreateWithDefaultLockNamespace(sqlServerConnectionString, logger);
        //_sqlServerLockProvider = () => SqlServerDistributedLockProvider.CreateWithDefaultLockNamespace(sqlServerConnectionString, logger);
        _redisLockProvider = () => RedLockDistributedLockProvider.CreateWithDefaultLockNamespace(CreateRedLockDistributedLockFactory(redisConnectionString), logger);
        _inProcessLockProvider = () => new InProcessLockProvider("default", logger);
    }

    public IDistributedLockProvider GetProvider(string moniker)
    {
        switch (moniker)
        {
            case Monikers.InProcess:
                return _inProcessLockProvider.Invoke();
            case Monikers.Postgres:
                return _postgresLockProvider.Invoke();
            case Monikers.MySql:
                return _mySqlLockProvider.Invoke();
            case Monikers.SqlServer:
                return _sqlServerLockProvider.Invoke();
            case Monikers.RedLock:
                return _redisLockProvider.Invoke();
            default:
                throw new NotSupportedException($"Factory type '{moniker}' is not supported.");
        }
    }

    private static IDistributedLockFactory CreateRedLockDistributedLockFactory(string connectionString)
    {
        var redisOptions = ConfigurationOptions.Parse(connectionString);
        var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
        var redLockMuultiplexer = new RedLockMultiplexer(connectionMultiplexer);
        return RedLockFactory.Create(new List<RedLockMultiplexer> { redLockMuultiplexer });
    }
}
