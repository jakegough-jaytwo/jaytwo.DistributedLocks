using System.Threading.Tasks;
using jaytwo.DistributedLocks;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using Microsoft.AspNetCore.Mvc;

namespace DistributedLocksSampleApp.Controllers;

public class HomeController : Controller
{
    private readonly PostgresDistributedLockFactory _pgLockFactory;
    private readonly MySqlDistributedLockFactory _myLockFactory;
    private readonly RedLockDistributedLockFactory _redLockFactory;

    public HomeController(
        PostgresDistributedLockFactory pgLockFactory,
        MySqlDistributedLockFactory myLockFactory,
        RedLockDistributedLockFactory redLockFactory)
    {
        _pgLockFactory = pgLockFactory;
        _myLockFactory = myLockFactory;
        _redLockFactory = redLockFactory;
    }

    [Route("/pg/{key}")]
    public async Task PostgresLock(string key)
    {
        await using var pgLock = await _pgLockFactory.CreateLockAsync(key);
    }

    [Route("/my/{key}")]
    public async Task MySqlLock(string key)
    {
        await using var pgLock = await _myLockFactory.CreateLockAsync(key);
    }

    [Route("/red/{key}")]
    public async Task RedlLock(string key)
    {
        await using var redLock = await _redLockFactory.CreateLockAsync(key);
    }

    [Route("/")]
    public async Task<object> Index()
    {
        return new
        {
            postgres = await HealthCheck.RunHealthCheckAsync(() => _pgLockFactory.HealthCheckAsync()),
            mysql = await HealthCheck.RunHealthCheckAsync(() => _myLockFactory.HealthCheckAsync()),
            redLock = await HealthCheck.RunHealthCheckAsync(() => _redLockFactory.HealthCheckAsync()),
        };
    }
}
