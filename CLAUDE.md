# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## First-time setup

```bash
# Install local tools (Husky) and wire up the pre-commit hook
dotnet tool restore
dotnet husky install
```

## Build & Run

```bash
# Build the solution
dotnet build

# Run full stack (API + Worker + PostgreSQL)
docker-compose -f src/docker-compose.yml up

# Run API locally (http://localhost:5207)
dotnet run --project src/Anichron.API

# Run worker locally
dotnet run --project src/Anichron.Worker
```

## Testing

```bash
# Run the unit tests (the default suite)
dotnet test

# Run the integration tests (needs a running Docker daemon)
dotnet test -p:TestSuite=Integration

# Run both
dotnet test -p:TestSuite=All

# Run tests for a specific project
dotnet test src/SomeProject.Tests.Unit/SomeProject.Tests.Unit.csproj

# Run a single test
dotnet test --filter "FullyQualifiedName=Namespace.ClassName.MethodName"
```

An integration test project, or a filter matching integration tests, runs nothing without `-p:TestSuite=Integration` (or `All`).

**The git hooks are the pre-commit and pre-push gates; don't run them by hand as well.** `pre-commit` runs `scripts/tool-versions.sh` (the `dotnet-ef` tool in `.config/dotnet-tools.json` must have the same version as `Microsoft.EntityFrameworkCore`, so bump them together), `dotnet format --verify-no-changes` and a full build, and `pre-push` runs the whole unit suite (`.husky/task-runner.json`). While working, run the test projects or tests you touched. Don't run the full unit suite before committing or pushing; `git push` runs it, and a failure stops the push. No hook runs the integration tests or mutation testing, so run those yourself when your change needs them.

Test projects are recognised by name: `*.Tests.Unit` for unit tests and `*.Tests.Integration` for integration tests. `Directory.Build.props` applies the test SDK to both: xUnit v3, **NSubstitute** (not Moq), FluentAssertions, `System.IO.Abstractions.TestingHelpers`, and coverlet. `Directory.Build.targets` makes only the projects in the selected `TestSuite` (`Unit` by default, `Integration` or `All`) test projects, so `dotnet test` and Stryker never run the integration suite. Internal members are exposed to test projects via `InternalsVisibleTo`.

**Unit or integration?** A test belongs in `Anichron.Tests.Integration` only when its subject is behaviour that only real PostgreSQL has: `ExecuteUpdate`/`ExecuteDelete`, transactions, advisory locks, row locking, constraint and partial-index enforcement. Everything else is a unit test, including a query's SQL shape (assert on `ToQueryString()` with `AnichronDbContexts.NpgsqlModel()`). The integration project starts one PostgreSQL container per run (`PostgresServer`, the same image as `src/docker-compose.yml`). Each test gets its own database, from `CreateMigratedDatabaseAsync` (real migrations) or `CreateDatabaseAsync` (empty). Its CI job is `Integration tests`, and it runs outside mutation testing, so the members it covers keep their `// Stryker disable all` and ADR 0005 rows.

Shared test infrastructure lives in `Anichron.Testing`, referenced by the test projects only and tested by `Anichron.Testing.Tests.Unit`; it is excluded from mutation (ADR 0005, rule 4). Use it instead of a private copy:

- **Time**: drive time-dependent code through a `Timeline` — its `Clock` is the fake `TimeProvider`, `Record` notes collaborator calls, `Watch` owns the task under test, and `ExpectAsync(...)` states what happens next and fails at the first event that differs. **No real-time waits in tests** (`WaitAsync(timeout)`, `SemaphoreSlim.WaitAsync(timeout)`, `Task.Delay`, an `await` on code that can hang): a broken implementation must fail at once, not when a guess runs out. Work delegates and substitutes used in loops yield and honour cancellation, so a loop that loses its delay cannot spin on the test thread. Recurring background work goes through `RecurringWork` (Worker) and is tested there.
- **Logging**: `FakeLogger<T>` from `Microsoft.Extensions.Diagnostics.Testing`.
- **EF**: `AnichronDbContexts.InMemory(...)` for data, `AnichronDbContexts.NpgsqlModel()` for model and SQL assertions; pass `freshModel: true` wherever a test asserts on the model.
- **DI scopes**: `ScopeFactories.Providing(services => ...)`, a real container rather than substitutes.

`Anichron.Architecture.Tests.Unit` holds the architecture rules (ArchUnitNET): decisions about types and dependencies that fail the build instead of living as prose. Adding one: `docs/agents/design-process.md`, "Adding an architecture rule".

### Mutation testing

```bash
dotnet tool restore
scripts/mutation-changed.sh   # scoped: only C# files changed since origin/master, one Stryker run per project
cd src && TMPDIR="$(mktemp -d)" dotnet stryker   # full sweep; must run from src/, not the repo root
```

⚠️ **Never run `dotnet stryker` with the shared `TMPDIR`.** Stryker's MTP runner passes the active mutant through `$TMPDIR/stryker-mutant-<n>.txt`, a path every run on the machine shares, so two agents mutating at the same time corrupt each other's results (survivors that tests do kill, scores down to 0%). `mutation-changed.sh` isolates itself; give a direct run its own `TMPDIR` as above. See `docs/mutation-testing.md`.

`scripts/mutation-guard.sh` (a CI step) enforces the setup rules: every setting lives in `src/stryker-config.json`, so a local run judges exactly as CI does; the `mtp` runner (`vstest` silently reports 0.00% under xUnit v3, ADR 0001); thresholds at 100; no `--since`/`--with-baseline` (in a worktree they test 0 mutants and exit `0`); `--concurrency` only in the divergence job, `--mutate` only in `mutation-changed.sh`, and `--project` only there and in CI's per-project matrix; workflows run Stryker only through `scripts/stryker-sweep.sh` (restores first, fails when Stryker could not analyse a project and silently left it unmutated); an ADR 0005 entry for every Stryker directive and excluded path; and a `!**/<Name>/**` exclusion for exactly the projects that set `IsMutationTarget` to `false` (ADR 0010). When it fails, fix the setup, not the guard.

#### Surviving mutants: test first, exclude only when equivalent

The gate is **100%**: one surviving or uncovered mutant fails CI. Before finishing any change to production code, run `scripts/mutation-changed.sh` and work through every undetected mutant in the files you touched. It runs the unit suite first and stops on a failing test (Stryker would fail every project's run on it). It maps a changed `<Name>…Tests.cs` to the source it is named after (`docs/mutation-testing.md`, "The scoped local run") and exits `1` for a changed test file it cannot map; pass the source files that test covers, relative to `src/`, as arguments. A changed file in no mutation target project (`scripts/mutation-targets.sh`) is named as `not mutated` and skipped. The full sweep in CI is the authority; run `dotnet stryker` locally as well only when the change reaches beyond the files it touches (`stryker-config.json`, a shared base class, test doubles):

1. **Write a test.** A survivor is a missing test until proven otherwise. The test asserts behaviour that the mutant changes, with an expected value from an independent source (a literal, the spec), never recomputed the way the code does.
2. **Question an exclusion only for an *equivalent* mutant**: one where no input can make the original and the mutant behave differently. Prove it by naming why every input agrees (a square image, a value exactly at the bound, a branch that only logs), or by measuring it. "Hard to test" is not equivalence.
3. **Prefer a rewrite that removes the equivalent branch** without changing behaviour (a clamp instead of a guard, an early return instead of a logging-only `if`). Suppress only when no such rewrite exists.
4. **Suppress narrowly.** Add a bare `// Stryker disable once <Mutator>` before the statement, and a row in ADR 0005 saying why. The directive binds to the next syntax node and disables *every* mutation of that mutator on it, so first confirm no killable sibling mutation on the same node gets hidden; if one would, restructure instead. A whole-file exclusion is only for pure wiring with no branching logic, listed by path in `stryker-config.json` and in ADR 0005.
5. **Code that only real PostgreSQL can exercise** (transactions, `ExecuteUpdate`/`ExecuteDelete`) gets a per-member `// Stryker disable all` and an ADR 0005 row. It is not tested on SQLite or another stand-in engine.

Done when the scoped run reports `100.00%` (or nothing to mutate) and every new directive or exclusion has its ADR 0005 row. Lowering the thresholds is never the fix; the reason is in `docs/mutation-testing.md`, "Why there is no slack".

Mutants that flip between runs come from `static readonly` initialisers, EF's cached model, and EF's compiled-query cache (a killing assertion that does not depend on the soft-delete filter reads through `IgnoreQueryFilters()`, #312): build such data in a property or method, and build model-asserting test contexts with `EnableServiceProviderCaching(false)` (`AnichronDbContexts.NpgsqlModel()`, or `InMemory(freshModel: true)`). An assertion behind `?.` (`x?.Value.Should()...`) never runs when `x` is null; assert presence first (`ContainSingle(...).Which`). Rationale for every existing exclusion: `docs/adr/0005-mutation-scope-excludes-unkillable-and-wiring-code.md`.

## Architecture

Four projects with strict separation of concerns:

| Project | Role |
|---------|------|
| `Anichron.Core` | Single source of truth: domain models, `AnichronDbContext`, all Fluent API config, shared utilities (XXHash64, path parsing). Zero dependencies on other projects (every other project references Core, so a reference back would be circular and fail the build). |
| `Anichron.Infrastructure` | Shared host plumbing used by API and Worker: configuration (`DatabaseConfiguration` reads the connection from `POSTGRES_CONNECTION__*` env vars or Docker secrets; INI bootstrap; `ConfigurationList`), migration under a PostgreSQL advisory lock, and the API's read-only stores (`IProxyStore`, `IOriginalStore`). Each host wires its own DI. |
| `Anichron.API` | ASP.NET Core Minimal APIs (the only shipped web project: `libraries-are-not-web` keeps ASP.NET Core out of Core, Infrastructure and Worker, and the two hosts never use each other) — owns all identity/auth/admin writes (users, storage configs, refresh tokens, invites, lockout); read-only for media data. Media endpoints so far: proxy serving (#43), On This Day (`GET /api/v1/flashbacks/on-this-day`, #38), a burst's members (`GET /api/v1/bursts/{id}/assets`, #299) and the caller's star/like/hide state (`PATCH /api/v1/assets/{id}/interaction`, #46); originals (#44) are deferred past v1.0. All routes prefixed `/api/v1/`. |
| `Anichron.Worker` | `BackgroundService`s — NAS crawling, EXIF extraction, FFmpeg transcoding, proxy generation, burst detection, reconciliation (soft-delete and auto-restore), admin-requested proxy sweeps and deletions, and the work queue's executor and scheduler (maintenance such as expired-refresh-token cleanup runs as work items) |

The API's HTTP surface is `AddApiSurface` plus `MapApiEndpoints`; `Program.cs` adds the host operations around it (INI bootstrap, start-up validation, migration, seeding). JWT and database settings are options validated at start, so registering the surface needs no secret. The non-shipped `Anichron.OpenApiGenerator` composes only the surface and writes `docs/openapi/v1.json`, the document the UI's client is generated from; `scripts/derive-openapi.sh` regenerates it and CI fails with `--check` when it is stale. `GET /api/v1/healthz` is not in it: `MapHealthChecks` endpoints are invisible to ApiExplorer.

**Write ownership is split by domain, not centralized.**
- **Worker is the only writer of *media* data** — `MediaAsset`, `Metadata`, `ProxyFile`, `Burst`, `BurstCheck`, plus all reconciliation and soft-deletes happen here. The API never writes media: outside its DI wiring it never touches `AnichronDbContext` (architecture rule `api-reaches-data-through-repositories`), and the media write repository stays out of it (`only-worker-writes-media`). Its one identity-side write is the `TokenCleanup` work item's handler deleting expired refresh tokens.
- **API owns all *identity/auth/admin* writes** — registration, login/refresh-token issuance and revocation, lockout state, invites, and admin user/storage-config CRUD all persist through API services (`AuthService`, `AdminUserService`, `AdminStorageConfigService`, `TokenService`, etc.) via `IUnitOfWork.SaveChangesAsync`. The API is read-only *only with respect to media*. The one exception is `AssetInteraction` (star/like/hide, #46): per-user state, written by the API through `IAssetInteractionWriter` (Core), which takes and returns plain records so no API type touches the entity. It checks only `VisibleTo` (never `NotHiddenFor`, so a hidden asset can be unhidden), and a first write that loses the `(UserId, AssetId)` unique race reloads and reapplies once.
- **Work items** are inserted only through `IWorkQueue` (architecture rule `only-work-queue-inserts-work-items`); the API's only other write is an admin's cancel through `IWorkItemCanceller` (`work-items-change-only-through-queue-or-cancel`: outside the Worker, only `EfWorkQueue`, `EfWorkItemReader` and `EfWorkItemCanceller` depend on the `WorkItem` entity; raw SQL naming the table is beyond the rule and is for review); every other state change belongs to the Worker's executor, and handlers live only in the Worker (`work-handlers-live-in-worker`). See `docs/architecture/work-queue.md`.
- **Proxy orphans** (`ProxyOrphans`, a proxy sweep's report) are written only by the Worker; outside it only `EfProxyOrphanReader` depends on the entity (`only-worker-writes-proxy-orphans`). See `docs/architecture/work-queue.md`, "Proxy sweeps".
- Both hosts apply EF migrations at startup, serialised by a PostgreSQL advisory lock (`MigrateWithAdvisoryLockAsync`).

A planned `Anichron.UI` (admin screens and the Instagram Story-style flashbacks) is not yet in the solution: a React + TypeScript single-page app built with Vite and served by the API on the same origin (ADR 0012). The work is planned in parent issues #344–#349.

### Storage Layout

```
/data/originals/   ← NAS (mounted :ro by both API and Worker — originals are never modified)
  <user>/          ← one mount per user, at the SAME container path in the API and that user's worker
/data/proxies/     ← Local SSD (Worker writes; API reads for serving) — fixed StoragePath.ProxiesRoot, not a setting, so both always agree
```

Proxy files follow a two-level shard path derived from **content, not from the ingestion attempt**: `/data/proxies/{content_hash[0:2]}/{storage_config_id:N}-{content_hash}/{type}` (e.g. `01/3fa85f6457174562b3fc2c963f66afa6-0123456789abcdef/thumbnail.jpg`). Re-ingesting the same file therefore resolves to the same directory, so a retry overwrites its own debris instead of stranding it; the storage config id keeps byte-identical files of two users apart, without which removing one user's config would cascade away the other's proxies. `IProxyDirectoryStrategy` is the single place this rule lives. The API serves them at `GET /api/v1/assets/{id}/proxies/{thumbnail|preview|video}` (visibility rule first, then only the stored `ProxyPath` under the proxy root; strong `ETag`, `private, immutable` caching, range requests). `ProxyFile.ProxyPath` stores the path relative to `/data/proxies/` (note: `FilePath` is on `MediaAsset`, relative to the storage config root — the two are different properties).

The API reads files only through two narrow stores in `Anichron.Infrastructure/Storage/`, never through `IFileSystem` (architecture rule `api-reads-no-file-system`). `IProxyStore.OpenRead(proxyPath)` owns the join with the proxy root, the containment check and "missing means `null`"; `IOriginalStore` has only `RootExists()` until serving originals (#44) adds its first reader. Neither has a write method. A new kind of read is a new store method with its own tests, not a general file system. See ADR 0007.

### Feature references (`docs/architecture/`)

Each file is the reference for its area. Read it before changing that area, and update it in the same change.

- **Data model** (`data-model.md`): the twelve entities, indexes, query filters, value converters, NodaTime. Read before adding an entity, column, index or migration; `docs/schema.sql` stays the authority on columns.
- **Ingestion** (`ingestion.md`): crawl, classification outcomes, reconciliation (soft-delete, auto-restore, Live Photo as one unit), proxies, burst detection, duplicate reports, failure ownership. Read before touching `Anichron.Worker/Ingestion`, `Crawling` or `Bursts`.
- **Work queue** (`work-queue.md`): producing, scope, scheduling, executing, cancelling, the admin view, adding a kind, the handler contract, and proxy sweeps. Read before adding a work kind or touching `Anichron.Core/Work` or `Anichron.Worker/Work`.
- **Media reads** (`media-reads.md`): `IAssetViewReader`, selections, burst covers, published media types, and the hidden/starred/liked rules. Read before adding or changing a media endpoint.

## Conventions

- **Enforced by the build** (`TreatWarningsAsErrors`, `src/.editorconfig`, central package management) and by `dotnet format --verify-no-changes`: file-scoped namespaces matching the folder, `camelCase` private fields (static ones too) with no underscore in production code (test projects suppress `IDE1006`), `I`-prefixed interfaces, 4-space indentation with LF line endings, `AnalysisMode=Recommended`, and package versions only in `Directory.Packages.props`. When one fires, fix the code rather than suppress the rule.
- **XML doc comments**: write `/// <summary>` only when it adds information beyond the name. Self-explanatory members (e.g. `DefaultPort`, simple repository methods) go without, overriding any global rule that asks for docs on every public member. Once a member has a summary, RCS1141 requires a `<param>` for each parameter; those short ones (`ct`: "Cancels the read.") are accepted, not a review finding.

## Multi-User Design

Each user has a private set of `UserStorageConfig` entries pointing to NAS roots. `RootPath` is globally unique — a path cannot be assigned to more than one storage config across all users. `UserStorageConfig → MediaAsset` scopes assets to a user's assigned NAS root. `RootPath` is a **container** path read by both the Worker (crawling) and the API (serving originals), so it must resolve to the same folder in every container: each user's NAS folder is mounted at `/data/originals/<user>` everywhere, and only the host side of the mount varies with the NAS layout. Either one worker per user (`WORKER__USER` + `WORKER__ROOTPATH=/data/originals/<user>`, which has no default and is required with `WORKER__USER`, mounting only that user's folder) or one all-user worker (no `WORKER__USER`, configs added through the admin API). Each user has private `AssetInteraction` state — never shared.

**Media is private per user.** Every media read goes through one visibility rule (`MediaAssetVisibility.VisibleTo(userId)`): an asset is visible only to the owner of its storage config, only while that config is active, and never once soft-deleted — `VisibleTo` checks all three itself, so it holds even under `IgnoreQueryFilters()`. It covers the asset only: related data needs its own filter (`Interactions` to the caller's rows, `Burst` members through `VisibleTo` again). Endpoints map "not visible" to **`404`, never `403`**, so other users' asset ids cannot be probed. Admins get no media access — their powers cover identity and storage configs only.

## Infrastructure

- Worker Dockerfile targets `mcr.microsoft.com/dotnet/runtime:10.0-noble` (not Alpine); published as multi-arch (`linux/amd64` + `linux/arm64`). `intel-media-va-driver-non-free` installed on `amd64` only; GPU fallback handles its absence at runtime. OpenCvSharp's native library needs `libatomic1` on `amd64` and `libtesseract5` + `libwebpdemux2` on `arm64` (ADR 0009).
- API Dockerfile uses Alpine (no GPU/FFmpeg needed)
- CI/CD runs on GitHub Actions (`.github/workflows/ci.yml`); API and Worker images built in parallel; pushed only after `CI gate` passes, tagged `sha-<hash>` on every master build, `edge` on master, SemVer tags on releases
- Branch protection requires only `CI gate` and `CodeQL`. To make a new job required, add it to the gate's `needs`, never to branch protection. The gate's rule is `scripts/ci-gate.sh`
- Merging a PR: wait until CI has finished and `CI gate` and `CodeQL` are green (`gh pr checks <n> --watch`), then ask the maintainer to confirm. Never merge before CI passes, and never without that confirmation.
- PostgreSQL connection is never hardcoded — always read from `POSTGRES_CONNECTION__*` env vars or Docker secrets
- CORS allowed origins configured via `CORS__ALLOWEDORIGINS` env var (comma-separated); the same value switches the refresh cookie to `SameSite=None`. Leave empty for same-origin / reverse proxy deployments.
- `X-Forwarded-For` is honoured only from loopback or from addresses listed in `FORWARDEDHEADERS__KNOWNPROXIES` / `FORWARDEDHEADERS__KNOWNNETWORKS` (comma-separated IPs / CIDRs; `ForwardLimit = 1`). Rate limiting partitions on the resulting client IP, so a reverse-proxy deployment must set one of these or all clients share the proxy's bucket. Never trust the Docker bridge network for a directly published port — that lets any client spoof its IP.
- Registration requires an admin-issued invite token. The first user (bootstrap admin) is exempt.
- Observability: `GET /api/v1/healthz` (database, proxy storage, originals storage; a missing mount reports Degraded). Logging is the default .NET console logger with `LoggerMessage` source-generated messages (architecture rule `logging-is-source-generated`; `BeginScope` is allowed); structured JSON output and OpenTelemetry are **not** wired up.
- License: AGPL-3.0.

## GitHub References

- **Issues** (bug reports, feature requests, epic tracking): https://github.com/bitbiter-dev/anichron/issues
- **Wiki** (early architecture decisions, e.g. ADR-3 on flashback weighting, which older issues link to): https://github.com/bitbiter-dev/anichron/wiki. New ADRs go in `docs/adr/`.
- **What to work on next**: the v1.0 order lives on GitHub, not here — each epic milestone's description lists its sequence, and each issue carries native *blocked by* links. An open "Architecture review due" issue goes before anything else (procedure: `docs/agents/design-process.md`, "Architecture review"). Otherwise pick an open `ready-for-agent` issue with no open blocker. A `needs-design` issue is picked only to write or approve its design note; a `needs-decision` issue waits for the maintainer.

## Code graph (who uses a symbol)

To find what calls, references or implements a C# symbol, ask the graph before grepping:

```bash
scripts/graph.sh affected <Symbol>   # everything that depends on it, tests included (depth 2, file:line)
scripts/graph.sh explain <Symbol>    # its direct callers and callees
```

It builds or refreshes the local, gitignored `graphify-out/` itself (seconds, no LLM). Use symbol names; free-text `graphify query` and `GRAPH_REPORT.md` are noisy and not worth the tokens. Grep stays the tool for text, config and anything that is not a symbol.

## Engineering process (objection, first caller, design note)

Agents write issues, designs and code; the maintainer sets requirements in conversation and makes decisions. Before writing an issue, designing, implementing or reviewing, read `docs/agents/design-process.md`. Its three load-bearing terms:

- **Objection:** when the requirement, design or spec is wrong for the system, object on the issue, relabel it and stop.
- **First caller:** every building block ships with the code that first uses it.
- **Design note:** issues that change the system's shape get one, approved before `ready-for-agent`.

## Agent skills

### Issue tracker

Issues live as GitHub issues in `bitbiter-dev/anichron`, driven via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical triage roles, each label string equal to its name, plus the repo's `needs-design` and `needs-decision`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context — one `GLOSSARY.md` (not created yet; written lazily by `/domain-modeling`) plus `docs/adr/` at the repo root. See `docs/agents/domain.md`.
