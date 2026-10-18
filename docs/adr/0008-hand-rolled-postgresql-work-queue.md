# A hand-rolled PostgreSQL work queue, not a job library

Status: accepted (2026-10-18, #276).

Work the Worker does on request or on a schedule is a row in `WorkItems`, in our EF model. A Worker
claims a row with `FOR UPDATE SKIP LOCKED`, holds it under a lease that a heartbeat extends, and
records the outcome in the same row. No job library is used.

## What it has to do

The API and the Worker share only the database. The queue has to let the API ask for work that only
a Worker can do, route a request to a Worker that has the right folder mounted (a per-user worker
sees one user's originals), recover an item whose Worker died, and give an admin a list of what is
queued, running and finished, with progress, from our own API.

## Libraries considered

The full evaluation is on #273. In short:

| Library | Why not |
| --- | --- |
| Hangfire + Hangfire.PostgreSql | Feasible, but its schema lives outside the EF model: not in `docs/schema.sql`, no `HasComment` (ADR 0004), created at startup beside our advisory-locked migrations. Jobs are serialised method calls on types both hosts share, so a rename breaks queued items. Newtonsoft.Json and `DateTime` at its boundary. No progress model, and per-job queue routing on PostgreSQL was unverified. |
| TickerQ | Enqueue-in-one-host, run-in-another and scope routing are undocumented; EF-only crash recovery unclear. |
| Quartz.NET (clustered) | A scheduler, not a work queue: any node runs any job, so routing would need one scheduler per user. No progress. |
| Wolverine | A messaging framework and programming model for both hosts; no job-status model to list. |
| Rebus | A message bus without a status view. |
| MassTransit | Commercial since 2026. |

## Consequences

- One table, one model: `WorkItems` is in `docs/schema.sql`, its columns carry comments, and every
  time is NodaTime.
- We own the concurrency code. The claim, the fenced transitions and lease recovery are PostgreSQL-only,
  so they carry `// Stryker disable all` (ADR 0005) and are covered instead by contract tests on real
  PostgreSQL in `Anichron.Tests.Integration`: concurrent claimers, skip-locked, zombie attempts, the
  stall limit, release without a stall and dedup coalescing.
- Delivery is at least once. A handler starts from scratch and converges, and its side effects are
  safe to repeat. Every write a Worker makes about a running item is fenced by `(Id, Attempt)`, so an
  attempt that lost its lease changes nothing.
- Due-ness comes from the table (nothing queued or running with the kind's key, nothing finished
  within its interval), so no host keeps timer state and overdue work runs right after downtime.
- `LISTEN/NOTIFY`, priorities and automatic retry are left out; each can be added without changing
  the handler contract.
