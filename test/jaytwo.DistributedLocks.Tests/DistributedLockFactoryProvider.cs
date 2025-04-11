using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace jaytwo.DistributedLocks.Tests;

public class DistributedLockFactoryProvider
{
    private readonly Func<IDistributedLockFactory> _mySqlLockFactory;
    private readonly Func<IDistributedLockFactory> _postgresLockFactory;
    private readonly Func<IDistributedLockFactory> _redisLockFactory;

    public DistributedLockFactoryProvider(
        string mySqlConnectionString,
        string postgresConnectionString,
        string redisConnectionString)
    {
        _mySqlLockFactory = () => new MySqlDistributedLockFactory(mySqlConnectionString);
        _postgresLockFactory = () => new PostgresDistributedLockFactory(postgresConnectionString);
        _redisLockFactory = () => new RedLockDistributedLockFactory(CreateRedLockDistributedLockFactory(redisConnectionString));
    }

    public IDistributedLockFactory GetFactory(string moniker)
    {
        switch (moniker)
        {
            case Monikers.Postgres:
                return _postgresLockFactory.Invoke();
            case Monikers.MySql:
                return _mySqlLockFactory.Invoke();
            case Monikers.RedLock:
                return _redisLockFactory.Invoke();
            default:
                throw new NotSupportedException($"Factory type '{moniker}' is not supported.");
        }
    }

    private static RedLockNet.IDistributedLockFactory CreateRedLockDistributedLockFactory(string connectionString)
    {
        var redisOptions = ConfigurationOptions.Parse(connectionString);
        var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
        var redLockMuultiplexer = new RedLockMultiplexer(connectionMultiplexer);
        return RedLockFactory.Create(new List<RedLockMultiplexer> { redLockMuultiplexer });
    }
}
