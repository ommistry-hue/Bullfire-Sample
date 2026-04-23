<p align="center">
  <img src="https://raw.githubusercontent.com/ommistry-hue/Bullfire-Sample/main/assets/lockup.png" alt="Bullfire" width="360">
</p>

<p align="center">
  <b>Fast, Redis-backed background job queue and scheduler for .NET.</b>
</p>

<p align="center">
  <a href="https://github.com/ommistry-hue/Bullfire-Sample/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT"></a>
  <a href="https://www.nuget.org/packages/Bullfire"><img src="https://img.shields.io/nuget/v/Bullfire.svg" alt="NuGet"></a>
  <a href="https://www.nuget.org/packages/Bullfire"><img src="https://img.shields.io/nuget/dt/Bullfire.svg" alt="NuGet downloads"></a>
</p>

---

This repo contains:

1. **📚 Documentation** for the Bullfire library — see the [`docs/`](./docs) folder.
2. **🚀 A runnable sample app** (this ASP.NET Core MVC project) that demonstrates the full
   end-to-end flow: schedule a status change from a web form, then watch a Bullfire
   background worker change the status automatically at the scheduled time.

If you're looking to **install and use** Bullfire in your own project, jump to [Install](#install).
If you want to **see it in action**, jump to [Running the sample](#running-the-sample).

---

## What is Bullfire?

Bullfire is a production-ready **background job processing library for .NET**. You push work
into a queue from anywhere in your app; workers running in the same process — or in a
separate service, on a different server — pick up those jobs and execute them reliably.

Every core capability you expect from a mature job-queue system ships in the free, MIT-licensed
package: **retries with backoff**, **priorities**, **delayed jobs**, **cron schedulers**,
**parent/child job trees**, **rate limiting**, **stalled-job recovery**, and **OpenTelemetry
tracing/metrics**. No paid tier, no "Enterprise Edition".

## When to use it

- Send emails / notifications **outside the web request/response cycle**
- Process file uploads, images, videos, or any long-running work asynchronously
- Run **scheduled reports, cleanups, syncs** on a cron schedule
- Build **multi-step workflows** where a parent job waits for many children to finish
- Enforce **rate limits** when calling paid or rate-limited third-party APIs
- Anything you would normally call a "job queue", "task queue", or "work queue"

## How it works

```
   ┌────────────────────┐                        ┌────────────────────┐
   │  Your application  │                        │      Redis         │
   │                    │   queue.AddAsync(...)  │                    │
   │   (producer)       │ ───────────────────▶   │   Atomic Lua writes│
   │                    │                        │   state buckets +  │
   │                    │                        │   event stream     │
   └────────────────────┘                        └──────────▲─────────┘
                                                            │
                                                            │ blocking fetch
   ┌────────────────────┐                                   │
   │  Worker service    │                                   │
   │                    │   Worker<TData> main loop ────────┘
   │  IJobHandler<T>    │
   │  (runs with scoped │   Lock auto-renewed on timer.
   │   DI per job)      │   Stalled jobs recovered.
   │                    │   Events emitted.
   └────────────────────┘
```

1. **Producer** calls `queue.AddAsync("my-job", data, options)`. Bullfire runs a single Lua
   script that atomically stores the job's data, puts its id in the right state bucket
   (waiting / delayed / prioritized), and writes an `added` event to the Redis Stream.
2. **Worker** runs an infinite loop that atomically pulls the next eligible job from Redis,
   takes out a lock, and invokes your `IJobHandler<TData>.HandleAsync(context, ct)` inside
   a **fresh DI scope** (so each invocation gets its own `DbContext`, `HttpClient`, etc.).
3. While the handler runs, Bullfire **renews the lock** every 15 seconds on a dedicated
   timer. If the worker process dies mid-job, the stalled-detection tick recovers the job
   and re-queues it automatically.
4. When the handler **succeeds**, the return value is written, the id moves to `completed`,
   and a `completed` event fires. If it **throws** and attempts remain, it's re-queued with
   exponential backoff; if attempts are exhausted, it moves to `failed`.
5. Every state transition emits to the queue's Redis Stream so dashboards and monitoring
   can observe the system in real time.

## Features

| Capability | In MIT core | Notes |
|---|---|---|
| Enqueue (standard / delayed / prioritized / bulk) | ✅ | Atomic via Lua |
| Worker loop + lock renewal + stalled detection | ✅ | 30s lock, 15s renew, 30s stalled tick |
| Retries with backoff | ✅ | Fixed, exponential-with-jitter, or custom `IBackoffStrategy` |
| Rate limiting | ✅ | Fixed-window |
| `RemoveOnComplete` / `KeepLast(N)` truncation | ✅ | |
| Cron schedulers | ✅ | Standard 5-field cron |
| Parent/child job flows (`FlowProducer`) | ✅ | Arbitrary depth; parent runs only after all children succeed |
| Event stream (`QueueEvents`) | ✅ | Real-time state-transition notifications |
| DI / HostedService / ILogger integration | ✅ | Standard ASP.NET Core patterns |
| OpenTelemetry `ActivitySource` + `Meter` | ✅ | Source/meter name: `"Bullfire"` |
| `IHealthCheck` | ✅ | Redis reachability + backlog threshold |
| Web UI dashboard | ✅ | Separate package `Bullfire.Dashboard` |

## Install

```
dotnet add package Bullfire
dotnet add package Bullfire.Dashboard   # optional: the Web UI
```

**Target frameworks:** `net8.0`, `net9.0`, `net10.0`.

**Runtime dependencies:** `StackExchange.Redis` + Microsoft's standard `.Extensions.*`
abstractions. All MIT, all free.

## Quick start — Producer

```csharp
using Bullfire;
using StackExchange.Redis;

using var mux = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var connection = new BullfireRedisConnection(mux);

var queue = new Queue("emails", connection);

// Simple job
await queue.AddAsync("send-welcome", new WelcomeEmail(userId: 42));

// Delayed, prioritized, retried
await queue.AddAsync("send-reminder", new Reminder(), new JobOptions
{
    DelayMilliseconds = 60_000,
    Priority = 5,
    Attempts = 3,
    Backoff = new BackoffOptions { Type = "exponential", DelayMilliseconds = 1_000 },
});
```

## Quick start — Worker

```csharp
using Bullfire;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddBullfire("localhost:6379");
builder.Services.AddBullfireWorker<WelcomeEmailHandler, WelcomeEmail>("emails");

builder.Build().Run();

public sealed record WelcomeEmail(int UserId);

public sealed class WelcomeEmailHandler : IJobHandler<WelcomeEmail>
{
    public Task HandleAsync(JobContext<WelcomeEmail> ctx, CancellationToken ct)
    {
        Console.WriteLine($"Welcome user {ctx.Data.UserId}");
        return Task.CompletedTask;
    }
}
```

## Quick start — Dashboard

```csharp
builder.Services.AddBullfireDashboard(opts =>
{
    opts.Queues.Add("emails");
});

// after building `app`
app.MapBullfireDashboard("/bullfire");
```

Navigate to `/bullfire` in your app and see a live-refreshing overview of every queue's state.

## Running the sample

The sample app in this repo shows everything above tied together in ~200 lines of C#.

**Prerequisites:**
- .NET 10 SDK
- A Redis-compatible server on `localhost:6379` ([Memurai](https://www.memurai.com/get-memurai) on Windows; `redis-server` on Mac/Linux; `docker run -d -p 6379:6379 redis:7-alpine`)

**Run it:**
```
git clone https://github.com/ommistry-hue/Bullfire-Sample.git
cd Bullfire-Sample
dotnet run
```

Open:
- **http://localhost:5228/** — the app. Click "+ New", pick a target status and a time 30s in the future, submit.
- **http://localhost:5228/bullfire** — the live Bullfire dashboard. Watch the job flow `Delayed → Active → Completed`.

Watch the item on the app page flip from `Pending` to your chosen target status at the scheduled time — done by a Bullfire background worker, not by any web request.

## Documentation

- **[Getting started](./docs/getting-started.md)** — step-by-step setup for any .NET app
- **[Production checklist](./docs/production-checklist.md)** — Redis tuning, TLS, concurrency, observability, deploy strategy

## License

MIT. See [LICENSE](./LICENSE).

## Questions / issues

Open an issue on this repo. The library source is maintained privately; bug reports and
feature requests for the core library go here.
