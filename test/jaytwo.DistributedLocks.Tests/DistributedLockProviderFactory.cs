using jaytwo.DistributedLocks.InMemory;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using RedLockNet;
using RedLockNet.SERedis;
using RedLockNet.SERedis.Configuration;
using StackExchange.Redis;

namespace jaytwo.DistributedLocks.Tests;

public class DistributedLockProviderFactory
{
    private readonly Func<IDistributedLockProvider> _mySqlLockProvider;
    private readonly Func<IDistributedLockProvider> _postgresLockProvider;
    private readonly Func<IDistributedLockProvider> _redisLockProvider;

    public DistributedLockProviderFactory(
        string mySqlConnectionString,
        string postgresConnectionString,
        string redisConnectionString)
    {
        _mySqlLockProvider = () => new MySqlDistributedLockProvider(mySqlConnectionString);
        _postgresLockProvider = () => new PostgresDistributedLockProvider(postgresConnectionString);
        _redisLockProvider = () => new RedLockDistributedLockProvider(CreateRedLockDistributedLockFactory(redisConnectionString));
    }

    public IDistributedLockProvider GetProvider(string moniker)
    {
        switch (moniker)
        {
            case Monikers.InMemory:
                return new InProcessLockProvider(nameof(DistributedLockProviderFactory));
            case Monikers.Postgres:
                return _postgresLockProvider.Invoke();
            case Monikers.MySql:
                return _mySqlLockProvider.Invoke();
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
