# A hand-rolled PostgreSQL work queue, not a job library

Status: accepted, amended by #337

## Decision

Work the Worker does on request or on a schedule is a row in `WorkItems`, in our EF model. A Worker claims a row with `FOR UPDATE SKIP LOCKED`, holds it under a lease that a heartbeat extends, and records the outcome in the same row. No job library is used.

Amendment (#337):

- **At most one item per storage config runs at a time, across all Workers.** The claim skips a config with a running item. A partial unique index on `StorageConfigId` where `Status = 'Running'` rejects a racing second claim.
- **An attempt that cannot reach the database stops itself** half a lease after its last heartbeat that got through, so it has stopped before another Worker can claim its config. Database writes are fenced; file side effects are not.

## Why

The API and Worker share only the database. The queue must route work to a Worker with the right folder mounted, recover work from a dead Worker, and give admins a list with progress through our own API.

## Rejected

Full evaluation on #273.

| Library | Why not |
| --- | --- |
| Hangfire + PostgreSql | Schema outside the EF model; jobs are serialised method calls, so a rename breaks queued items; no progress model |
| TickerQ | Cross-host enqueueing and routing undocumented |
| Quartz.NET | A scheduler, not a queue: routing would need one scheduler per user |
| Wolverine, Rebus | Messaging, with no job-status model |
| MassTransit | Commercial |

## Consequences

- One table in `docs/schema.sql`, commented and using NodaTime.
- We own the concurrency code. It is PostgreSQL-only, so it is excluded from mutation (ADR 0005) and covered by integration tests.
- Delivery is at least once. Handlers converge and are safe to repeat, and every write about a running item is fenced by `(Id, Attempt)`.
- Due-ness comes from the table, so overdue work runs right after downtime.
- A database outage longer than half a lease restarts running work and counts a stall. `WorkLeaseSeconds` must exceed four heartbeats.
- `LISTEN/NOTIFY`, priorities and automatic retry are left out; each can be added without changing the handler contract.
