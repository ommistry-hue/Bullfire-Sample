# Getting started with Bullfire

This guide walks through installing Bullfire, connecting to Redis, enqueueing jobs, and running a worker.

## 1. Install a Redis server

Bullfire uses Redis 5+ as its backing store. Any Redis-compatible server works.

- **Windows:** [Memurai Developer Edition](https://www.memurai.com/get-memurai) (free, installs as a Windows service)
- **macOS:** `brew install redis && brew services start redis`
- **Linux:** `apt install redis-server` (or your distro's equivalent)
- **Docker:** `docker run -d -p 6379:6379 redis:7-alpine`

Verify it's running:

```
memurai-cli ping   # Windows
redis-cli ping     # macOS / Linux
```

Both should reply `PONG`.

## 2. Add Bullfire to your project

```
dotnet add package Bullfire
```

## 3. Define a job payload + handler

Payloads are plain records. Handlers implement `IJobHandler<TData>` and resolve per-job from a
fresh DI scope — meaning every invocation gets its own `DbContext`, `HttpClient`, etc., just
like an ASP.NET Core controller action.

```csharp
public sealed record SendEmailJob(string To, string Subject, string Body);

public sealed class SendEmailHandler : IJobHandler<SendEmailJob>
{
    private readonly IEmailGateway _gateway;
    private readonly ILogger<SendEmailHandler> _logger;

    public SendEmailHandler(IEmailGateway gateway, ILogger<SendEmailHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task HandleAsync(JobContext<SendEmailJob> ctx, CancellationToken ct)
    {
        _logger.LogInformation("Sending email {JobId} (attempt {N})",
            ctx.JobId, ctx.AttemptsMade);
        await _gateway.SendAsync(ctx.Data.To, ctx.Data.Subject, ctx.Data.Body, ct);
    }
}
```

## 4. Register Bullfire with DI

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddBullfire("localhost:6379");
builder.Services.AddBullfireWorker<SendEmailHandler, SendEmailJob>("emails");

// ...and whatever else (email gateway, db, etc.)
builder.Services.AddScoped<IEmailGateway, SmtpEmailGateway>();

builder.Build().Run();
```

The worker starts automatically with the host and stops gracefully on shutdown.

## 5. Enqueue jobs

From anywhere in your app (e.g. a web controller):

```csharp
public class EmailsController(Queue queue) : ControllerBase
{
    [HttpPost]
    public async Task<string> SendWelcome(int userId)
    {
        return await queue.AddAsync("welcome", new SendEmailJob(
            To: $"user-{userId}@example.com",
            Subject: "Welcome!",
            Body: "Thanks for joining."));
    }
}
```

Bullfire auto-registers `Queue` per-queue name. You can also construct one explicitly:

```csharp
var queue = new Queue("emails", connection);
var jobId = await queue.AddAsync("welcome", new SendEmailJob(...));
```

## Common options

```csharp
await queue.AddAsync("name", data, new JobOptions
{
    DelayMilliseconds = 30_000,                              // fire in 30s
    Priority = 1,                                            // 1 = highest (FIFO within tier)
    Attempts = 5,                                            // retry up to 5 total
    Backoff = new BackoffOptions
    {
        Type = "exponential",                                // or "fixed"
        DelayMilliseconds = 1_000,
    },
    RemoveOnComplete = RemoveOnCompletion.KeepLast(100),     // keep last 100 completed
    RemoveOnFail     = RemoveOnCompletion.KeepLast(1000),    // keep last 1000 failed
    JobId = "custom-id-per-business-key",                    // dedup via your own key
});
```

## Observability

Bullfire ships `ActivitySource "Bullfire"` + `Meter "Bullfire"` out of the box. Wire into OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("Bullfire"))
    .WithMetrics(m => m.AddMeter("Bullfire"));
```

You now get spans around `bullfire.enqueue` and `bullfire.process`, plus counters for
jobs.enqueued / completed / failed / retried, a jobs.active gauge, and a jobs.duration histogram.

## Health checks

```csharp
builder.Services
    .AddHealthChecks()
    .AddCheck<Bullfire.HealthChecks.BullfireHealthCheck>("bullfire");
```

The check pings Redis and optionally monitors queue backlog.

## Next steps

- [Migrating from Hangfire](./migration-from-hangfire.md)
- [BullMQ compatibility notes](./bullmq-compat-notes.md)
- [Production checklist](./production-checklist.md)
