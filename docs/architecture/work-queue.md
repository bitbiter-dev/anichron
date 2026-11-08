# Work queue

Durable Worker work in `WorkItems` (ADR 0008). Code: `Anichron.Core/Work`, `Anichron.Worker/Work`.

Kinds: `Crawl` and `BurstDetection` (one per storage config), `TokenCleanup` and `WorkItemRetention` (maintenance), `ProxySweep` and `ProxyDeletion` (admin-requested).

## Lifecycle

- **Producing:** `IWorkQueue.EnqueueAsync` is the only insert. A `DedupKey` coalesces: while an item with that key is queued, enqueueing returns it.
- **Scope:** an item may carry a `StorageConfigId`. A per-user Worker claims unscoped items and its user's configs; an all-user Worker claims everything.
- **Scheduling:** `WorkScheduler` polls `IWorkScheduleSource`s. A schedule is due when nothing with its key is queued or running and nothing finished within its interval. No timer state, so overdue work runs right after downtime.
- **Executing:** `WorkExecutor` claims the oldest eligible item (`FOR UPDATE SKIP LOCKED`) under a lease that a heartbeat extends.
  - Every write about a running item is fenced by `(Id, Attempt)`.
  - **At most one item per storage config runs at a time, across all Workers.** A partial unique index backs the claim.
  - **Self-stop:** an attempt whose heartbeats have not reached the database for half a lease cancels itself, so no two attempts touch one config's files at once. `WorkLeaseSeconds` must exceed four heartbeats.
  - An expired lease requeues the item and counts a stall; the `WorkMaxStalls`th fails it. A shutdown releases it without a stall. A handler exception fails it; there are no retries.
  - A Worker claims only the kinds it knows.
- **Cancelling:** `POST /api/v1/admin/work-items/{id}/cancel` through `EfWorkItemCanceller`. A queued item ends `Cancelled` at once; a running one gets a request that the heartbeat passes to the handler, and it ends `Cancelled` with partial progress. A cancelled periodic item counts as that run.
- **Admin view:** list, detail and a Server-Sent Events stream keyed on `UpdatedAt`. The list returns the cursor to subscribe from, so nothing is missed. The API reads items only through `IWorkItemReader`.
- **Retention:** `WorkItemRetention` deletes finished items after `WorkItemRetentionDays`.

## Adding a kind

1. A `WorkKind` value.
2. A payload record in Core implementing `IWorkPayload` (static `Kind`, optional `DedupKey`).
3. An `IWorkHandler<TPayload>` in the Worker, registered with `AddWorkHandler`.
4. If periodic: a `WorkSchedule<TPayload>` registered as an `IWorkScheduleSource`. A per-config kind implements `IStorageConfigPayload<TSelf>` and registers `StorageConfigSchedules<TPayload>`.

`WorkHandlerRegistry` fails startup unless every kind has exactly one payload and one handler.

**Handler contract:** delivery is at least once. A run starts from scratch, converges, and its side effects are safe to repeat. The returned summary becomes `Result`.

## Proxy sweeps

Orphaned proxy files are reported, never deleted unattended.

1. `POST /api/v1/admin/proxy-sweeps` enqueues a `ProxySweep`. It records in `ProxyOrphans` every file under the proxy root that no `ProxyFile` row references (soft-deleted assets' included) and that is older than an hour.
2. `GET .../{sweepId}/orphans` pages the report.
3. `POST .../{sweepId}/deletions` enqueues one `ProxyDeletion` per live config the orphans name, plus one unscoped item for the rest. "Live" has one definition: `LiveStorageConfigs.LiveIds`.
4. `ProxyDeletionHandler` re-checks each orphan just before deleting it (still unreferenced, present, same size and write time) and records the outcome. It removes an emptied content folder, never a shard folder.
