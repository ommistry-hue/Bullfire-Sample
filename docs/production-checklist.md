# Production checklist

Run through this list before deploying Bullfire to production.

## Redis / Memurai

- [ ] Redis or Memurai is version 5 or newer (for Streams support).
- [ ] Persistence configured (RDB snapshots, AOF, or both).
- [ ] Maxmemory policy set to `noeviction` (evicting a job hash mid-run is catastrophic).
- [ ] Replica + sentinel (or Redis Cluster) for failover if downtime matters.
- [ ] TLS enabled on the connection string (`,ssl=true`).
- [ ] Password / ACL auth on the connection string (`,password=...`).
- [ ] If using Redis Cluster: queue names are wrapped with hash-tag braces (Bullfire does this automatically; verify via `redis-cli`).

## Bullfire configuration

- [ ] `BullfireOptions.MaxEventsLength` sized for your consumer lag tolerance (default 10,000).
- [ ] `WorkerOptions.Concurrency` tuned — start with 1, increase if handlers are I/O-bound.
- [ ] `WorkerOptions.LockDuration` > P99 handler runtime; default 30s works for most cases.
- [ ] `WorkerOptions.LockRenewTime` < `LockDuration / 2`; default 15s is fine.
- [ ] `HostOptions.ShutdownTimeout` >= `LockDuration + max handler runtime`. The default 30s is often too tight — set 60s+ for slow handlers.
- [ ] Rate limits on workers that call external paid APIs.

## Handler code

- [ ] Handlers are idempotent (retry delivers at-least-once).
- [ ] Handlers check `cancellationToken.ThrowIfCancellationRequested()` periodically in long work.
- [ ] Handlers don't swallow exceptions silently — the worker's retry logic depends on exceptions bubbling.
- [ ] DB transactions commit BEFORE the handler returns (not in a `finally`).

## Observability

- [ ] OpenTelemetry wired: `AddSource("Bullfire")` + `AddMeter("Bullfire")`.
- [ ] Dashboards show: `jobs.active` (gauge), `jobs.enqueued/completed/failed/retried` (counters), `jobs.duration` (histogram p50/p95/p99).
- [ ] Alerts on:
  - `jobs.active` > threshold (stuck workers)
  - `jobs.failed` rate spike
  - Redis ping failures (via `BullfireHealthCheck`)
  - Wait-list backlog > threshold (overload)

## Capacity

- [ ] Load-tested with realistic job size + rate. Target: 1-2× peak production load.
- [ ] Worker replicas = at least 2, deployed across availability zones.
- [ ] Redis memory sized for worst-case backlog (wait + delayed + completed history).

## Security

- [ ] Redis is not publicly routable.
- [ ] Job payloads don't log secrets (check your `ILogger` calls in handlers).
- [ ] If payloads contain PII, consider layering an encrypting `IJobSerializer` on top of `SystemTextJsonJobSerializer`.

## Deployment

- [ ] CI runs `dotnet test` against a real Redis (Testcontainers, Memurai on Windows runners, or shared Redis).
- [ ] Deploy producer and worker separately — producers scale with web traffic, workers with job backlog.
- [ ] Blue/green or rolling deploy. Old workers finish in-flight jobs, new workers pick up the next one (stalled-check handles any genuinely abandoned jobs).

## Gotchas

- **Do not share one `IConnectionMultiplexer` between a worker and unrelated blocking Redis code.** Bullfire's `Worker` opens its own dedicated connection for blocking fetches, but if you run other blocking commands on the same shared mux you may stall producers. Put blocking consumers on their own mux.
- **Do not use `--no-appendonly yes` in production Redis.** RDB alone can lose up to 60s of data on crash; AOF brings it down to a second.
