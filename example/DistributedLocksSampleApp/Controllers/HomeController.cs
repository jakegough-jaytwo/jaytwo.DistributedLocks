using System.Threading.Tasks;
using jaytwo.DistributedLocks;
using jaytwo.DistributedLocks.MySql;
using jaytwo.DistributedLocks.Postgres;
using jaytwo.DistributedLocks.RedLock;
using Microsoft.AspNetCore.Mvc;

namespace DistributedLocksSampleApp.Controllers;

public class HomeController : Controller
{
    private readonly PostgresDistributedLockProvider _pgLockProvider;
    private readonly MySqlDistributedLockProvider _myLockProvider;
    private readonly RedLockDistributedLockProvider _redLockProvider;

    public HomeController(
        PostgresDistributedLockProvider pgLockProvider,
        MySqlDistributedLockProvider myLockProvider,
        RedLockDistributedLockProvider redLockProvider)
    {
        _pgLockProvider = pgLockProvider;
        _myLockProvider = myLockProvider;
        _redLockProvider = redLockProvider;
    }

    [Route("/pg/{key}")]
    public async Task PostgresLock(string key)
    {
        await using var pgLock = await _pgLockProvider.CreateLockAsync(key);
    }

    [Route("/my/{key}")]
    public async Task MySqlLock(string key)
    {
        await using var pgLock = await _myLockProvider.CreateLockAsync(key);
    }

    [Route("/red/{key}")]
    public async Task RedlLock(string key)
    {
        await using var redLock = await _redLockProvider.CreateLockAsync(key);
    }

    [Route("/")]
    public async Task<object> Index()
    {
        return new
        {
            postgres = await HealthCheck.RunHealthCheckAsync(() => _pgLockProvider.HealthCheckAsync()),
            mysql = await HealthCheck.RunHealthCheckAsync(() => _myLockProvider.HealthCheckAsync()),
            redLock = await HealthCheck.RunHealthCheckAsync(() => _redLockProvider.HealthCheckAsync()),
        };
    }
}
