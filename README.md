# jaytwo.DistributedLocks

| Package |   |   |
|---------|---|---|
| `jaytwo.DistributedLocks` | [![NuGet Version](https://img.shields.io/nuget/v/jaytwo.DistributedLocks.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.DistributedLocks) | [![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.DistributedLocks.svg?style=flat)](https://www.nuget.org/packages/jaytwo.DistributedLocks) |
| `jaytwo.DistributedLocks.Postgres` | [![NuGet Version](https://img.shields.io/nuget/v/jaytwo.DistributedLocks.Postgres.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.DistributedLocks.Postgres) | [![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.DistributedLocks.Postgres.svg?style=flat)](https://www.nuget.org/packages/jaytwo.DistributedLocks.Postgres) |
| `jaytwo.DistributedLocks.MySql` | [![NuGet Version](https://img.shields.io/nuget/v/jaytwo.DistributedLocks.MySql.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.DistributedLocks.MySql) | [![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.DistributedLocks.MySql.svg?style=flat)](https://www.nuget.org/packages/jaytwo.DistributedLocks.MySql) |
| `jaytwo.DistributedLocks.RedLock` | [![NuGet Version](https://img.shields.io/nuget/v/jaytwo.DistributedLocks.RedLock.svg?style=flat&logo=nuget)](https://www.nuget.org/packages/jaytwo.DistributedLocks.RedLock) | [![NuGet Downloads](https://img.shields.io/nuget/dt/jaytwo.DistributedLocks.RedLock.svg?style=flat)](https://www.nuget.org/packages/jaytwo.DistributedLocks.RedLock) |


**jaytwo.DistributedLocks** is a .NET abstraction layer for distributed locking across multiple providers including in-memory, MySQL, PostgreSQL, and Redis (via RedLock). It provides a consistent interface for acquiring and managing distributed locks in both sync and async contexts.

[![View on GitHub](https://img.shields.io/badge/View%20on-GitHub-181717?logo=github)](https://github.com/jakegough-jaytwo/jaytwo.DistributedLocks)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://mit-license.org/)

## Features

- Unified interface for multiple distributed lock backends
- Support for both `IDisposable` and `IAsyncDisposable`
- Customizable acquisition timeouts
- Built-in health checks with `HealthCheckAsync()`
- Available providers:
  - In-memory (single process)
  - MySQL
  - PostgreSQL
  - Redis ([RedLock.NET](https://github.com/samcook/RedLock.net))

> Only MySQL, PostgreSQL, and Redis are currently supported. SQL Server support is planned but not yet implemented.

## Installation

Install the NuGet package(s) that match your backend:

```powershell
PM> Install-Package jaytwo.DistributedLocks              # Core abstractions
PM> Install-Package jaytwo.DistributedLocks.MySql        # MySQL provider
PM> Install-Package jaytwo.DistributedLocks.Postgres     # PostgreSQL provider
PM> Install-Package jaytwo.DistributedLocks.RedLock      # Redis (RedLock.NET) provider
```

## Usage

### Acquire and Release a Lock

Locks are automatically released when disposed:

```csharp
await using (var myLock = await provider.CreateLockAsync("my-key"))
{
    if (!myLock.IsAcquired)
    {
        Console.WriteLine("Could not acquire lock");
        return;
    }

    // Critical section
}
```

### Set a Custom Wait Timeout

```csharp
await using var myLock = await provider.CreateLockAsync("my-key", waitTime: TimeSpan.FromSeconds(5));
```

### Health Check

You can check the health of the underlying lock provider. This is useful for monitoring and diagnostics:

```csharp
var healthCheckResult = await provider.HealthCheckAsync();
```

## Background

[RedLock.NET](https://github.com/samcook/RedLock.net) is great, but I don't love the polling nature of the RedLock algorithm, and many smaller apps aren’t connected to a Redis instance. Most SQL-based systems already support advisory locking or built-in mutex mechanisms. This library provides a consistent API across different database backends, giving you the flexibility to use what you already have deployed.

## When to Use

Use `jaytwo.DistributedLocks` when you need:

- Coordination across distributed services or processes
- A lightweight alternative to full orchestration frameworks
- Backend flexibility without rewriting business logic
- Locking via familiar SQL databases or Redis

Typical scenarios:

- Distributed job scheduling
- Coordinating background workers
- Ensuring single execution of batch jobs
- Preventing double-processing across instances

---

Made with &hearts; by Jake — Licensed under the [MIT License](https://mit-license.org/)
