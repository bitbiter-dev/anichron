# CLAUDE.md

Guidance for Claude Code (claude.ai/code) in this repository.

## First-time setup

```bash
# Git LFS stores every binary file (.gitattributes). --skip-repo: Husky's hooks call git lfs.
brew install git-lfs
git lfs install --skip-repo
git lfs pull                    # only if you cloned before the line above

dotnet tool restore             # local tools (Husky)
dotnet husky install            # git hooks

# The UI needs only pnpm; it downloads the pinned Node itself (devEngines.runtime)
pnpm --dir src/Anichron.UI install
```

## Build & Run

```bash
dotnet build
docker-compose -f src/docker-compose.yml up      # API + Worker + PostgreSQL
dotnet run --project src/Anichron.API            # http://localhost:5207
dotnet run --project src/Anichron.Worker
```

Building needs the Six Labors licence key in the gitignored `sixlabors.lic` at the repo root. Building the images: README, "Build the images".

## Testing

```bash
dotnet test                                   # unit tests (default)
dotnet test -p:TestSuite=Integration          # integration tests (needs Docker)
dotnet test -p:TestSuite=All
dotnet test src/SomeProject.Tests.Unit/SomeProject.Tests.Unit.csproj
dotnet test --filter "FullyQualifiedName=Namespace.ClassName.MethodName"
```

An integration project or filter runs nothing without `-p:TestSuite=Integration` (or `All`).

**The git hooks and CI are the gates; don't run the hooks by hand.**

- `pre-commit`: `scripts/tool-versions.sh` (`dotnet-ef` in `.config/dotnet-tools.json` must match `Microsoft.EntityFrameworkCore`; bump them together), `scripts/lfs-pointers.sh` (a new binary type goes in `.gitattributes`), `scripts/docs-guard.sh`, and `dotnet format whitespace` on the staged C# files.
- `pre-push`: only `git lfs pre-push`. CI builds and tests.
- The build enforces style and analyzer rules (`EnforceCodeStyleInBuild`); the full `dotnet format --verify-no-changes` runs only in CI.
- A commit in a worktree runs the main checkout's `task-runner.json` (`core.hooksPath` is absolute), so a hook change takes effect once it is on `master`.
- While working, run only the tests you touched. No hook runs integration or mutation tests; run those yourself when your change needs them.

**Test projects** are named `*.Tests.Unit` and `*.Tests.Integration`. `Directory.Build.props` gives them xUnit v3, **NSubstitute** (not Moq), FluentAssertions, `System.IO.Abstractions.TestingHelpers` and coverlet. Internals are visible to test projects.

**Unit or integration?**

- Integration (`Anichron.Tests.Integration`) only for behaviour that needs real PostgreSQL: `ExecuteUpdate`/`ExecuteDelete`, transactions, advisory locks, row locking, constraint and partial-index enforcement.
- Everything else is unit, a query's SQL shape included (`ToQueryString()` on `AnichronDbContexts.NpgsqlModel()`).
- Each integration test gets its own database: `CreateMigratedDatabaseAsync` (migrated) or `CreateDatabaseAsync` (empty).
- Members covered only there keep their `// Stryker disable all` and ADR 0005 row.

**Shared test infrastructure** is `Anichron.Testing` (tested by its own project, excluded from mutation). Use it instead of a private copy:

- **Time:** a `Timeline` (`Clock` is the fake `TimeProvider`, `Record`, `Watch`, `ExpectAsync`).
  - **No real-time waits in tests** (`Task.Delay`, timeouts, an `await` that can hang): a broken implementation must fail at once.
  - Delegates and substitutes used in loops yield and honour cancellation.
  - Recurring background work goes through `RecurringWork`.
- **Logging:** `FakeLogger<T>`.
- **EF:** `AnichronDbContexts.InMemory(...)` for data, `NpgsqlModel()` for model and SQL assertions; `freshModel: true` wherever a test asserts on the model.
- **DI scopes:** `ScopeFactories.Providing(...)`, a real container.

**Architecture rules** live in `Anichron.Architecture.Tests.Unit` (ArchUnitNET). Adding one: `.claude/docs/design-process.md`, "Adding an architecture rule".

### Mutation testing

```bash
scripts/mutation-changed.sh                        # changed files only, one Stryker run per project
cd src && TMPDIR="$(mktemp -d)" dotnet stryker     # full sweep, from src/
```

- ⚠️ **Never run `dotnet stryker` with the shared `TMPDIR`**: concurrent runs corrupt each other (`docs/mutation-testing.md`). `mutation-changed.sh` isolates itself.
- `scripts/mutation-guard.sh` (CI) enforces the setup: its rules are in its header. When it fails, fix the setup, not the guard.

#### Surviving mutants: test first, exclude only when equivalent

The gate is **100%**: one surviving or uncovered mutant fails CI. Before finishing a production change, run `scripts/mutation-changed.sh` and resolve every undetected mutant in the files you touched. How it maps tests to sources: `docs/mutation-testing.md`, "The scoped local run". Run the full sweep locally only when the change reaches beyond its files (`stryker-config.json`, a shared base class, test doubles).

1. **Write a test.** A survivor is a missing test until proven otherwise. Take the expected value from an independent source, never recomputed as the code does.
2. **Exclude only an equivalent mutant**: one where no input can tell the original from the mutant. Prove it. "Hard to test" is not equivalence.
3. **Prefer a rewrite** that removes the equivalent branch (a clamp instead of a guard, an early return instead of a logging-only `if`).
4. **Suppress narrowly:** a bare `// Stryker disable once <Mutator>` plus an ADR 0005 row. The directive covers every mutation of that mutator on the next syntax node, so check it hides no killable sibling. A whole-file exclusion is only for pure wiring, listed in `stryker-config.json` and ADR 0005.
5. **PostgreSQL-only code** gets a per-member `// Stryker disable all` and an ADR 0005 row. Never test it on SQLite or another stand-in.

Done when the scoped run reports `100.00%` (or nothing to mutate). Never lower the thresholds (`docs/mutation-testing.md`, "Why there is no slack").

Mutants that flip between runs:

- **`static readonly` initialisers** and **EF's cached model:** build such data in a property or method; model tests use `EnableServiceProviderCaching(false)` (`NpgsqlModel()`, `InMemory(freshModel: true)`).
- **EF's compiled-query cache:** an assertion that doesn't depend on the soft-delete filter reads through `IgnoreQueryFilters()`.
- **An assertion behind `?.`** never runs on null: assert presence first (`ContainSingle(...).Which`).

## The UI (`src/Anichron.UI`)

- **Commands** are the `package.json` scripts, run from `src/Anichron.UI`. `pnpm dev` proxies `/api` to `http://localhost:5207`.
- **API contract change** (an `ErrorCodes` constant included): run `scripts/derive-openapi.sh`, then `pnpm openapi`; CI fails on a stale `src/api/generated/v1.ts`. A screen's error messages are keyed by the server's `ErrorCode` and the client's `ValidationCode` (`src/api/problem.ts`).
- **Hooks:** a commit with a UI file runs prettier and ESLint on the staged files, and `typecheck` (incremental) and `depcruise` on the whole project.
- **CI:** `UI` runs all of them, plus the scoped mutation run and the bundle budget (`.size-limit.js`, over what `index.html` loads; a PR that raises a limit says why). `E2E` runs `pnpm e2e` against the PR's API image. Locally, build `anichron-api:e2e` and start `src/docker-compose.e2e.yml` (its header has the commands).

**Session:** before any route renders, the app refreshes once and holds the session and user in memory. `src/api/session.ts` renews it with one single-flight refresh on a schedule and when visible again. A `401` from refresh signs out; a `401` from logout refreshes and retries once. Other failed starts show `src/app/StartFailed.tsx`. Data calls live apart, one module per area (`docs/architecture/ui-api.md`).

**Tabs:** login, refresh and logout run under one Web Lock, and signing in or out is announced on a `BroadcastChannel`, so every other tab starts again or signs out. Both go through the port in `src/api/tabs.ts`, composed in `main.tsx`; tests use `src/test/tabs.ts`.

**Layout:** `src/app/` (entry, providers, `router.tsx`), `src/api/`, `src/i18n/`, `src/shared/`, `src/features/<name>/`, `src/test/`, `src/design/` (dev-only design page).

- dependency-cruiser forbids: a feature importing another feature; `shared/` or `api/` importing `features/` or `app/`; anything but `app/` importing a feature; cycles; production code importing test code or the design page; `.ts` importing `.tsx`.
- Only `src/api/` touches the network: ESLint bans `fetch`, `EventSource`, `XMLHttpRequest` and `WebSocket` elsewhere.

**Logic in `.ts`, views in `.tsx`.** Branching, computed values, timers and hooks without JSX go in `.ts`. ESLint caps `.tsx` at `complexity` 4 and `max-depth` 2. Every user-facing string goes through `t(...)` from `src/i18n/locales/en.json`; lint fails on a literal in JSX text or user-visible attributes.

**Tests.**

- Seam 2 is the default: `renderApp(path)` renders the real route tree, and MSW (`src/test/server.ts`) fails on any unhandled request.
- Assert what the user sees, plus `expect(await axeResults(container)).toHaveNoViolations()`.
- Timers are always fake.
- A test leaves no state behind: the suite runs shuffled in shared workers (`docs/mutation-testing.md`, "The UI"). A failure prints its seed; rerun with `--sequence.seed`.
- Test a `.ts` module directly (seam 3) only for a mutant seam 2 cannot reasonably kill.

**Mutation:** StrykerJS mutates `src/**/*.ts` and `src/app/router.tsx` at 100 (ADR 0014). Before finishing, run `scripts/ui-mutation-changed.sh`, with the same survivor rules as the backend; a suppression needs an ADR 0014 row. No private `TMPDIR` needed. `scripts/ui-mutation-guard.sh` (CI) guards the setup.

## Architecture

| Project | Role |
|---------|------|
| `Anichron.Core` | Domain models, `AnichronDbContext`, all Fluent config, shared utilities. References no other project. |
| `Anichron.Infrastructure` | Host plumbing shared by API and Worker: configuration (`POSTGRES_CONNECTION__*` or Docker secrets, INI bootstrap), migration under an advisory lock, the API's read-only stores. |
| `Anichron.API` | ASP.NET Core Minimal APIs under `/api/v1/`, the only web project (`libraries-are-not-web`). Owns identity, auth and admin writes; read-only for media. |
| `Anichron.Worker` | `BackgroundService`s: crawling, metadata, transcoding, proxies, bursts, reconciliation, and the work queue's executor and scheduler. |
| `Anichron.UI` | React + TypeScript SPA (Vite), served by the API image on the same origin (ADR 0012, `ui-serving.md`). |

**API surface.**

- `AddApiSurface` + `MapApiEndpoints` define the HTTP surface; `Program.cs` adds host operations around it.
- `Anichron.OpenApiGenerator` composes only the surface and writes `docs/openapi/v1.json` (`scripts/derive-openapi.sh`, checked by CI).
- Every route declares its responses (`ApiDocumentTests` pins them) and takes its requirements through `RequireUser`, `RequireAdmin` and `RequireRateLimit`.
- Every API error is Problem Details (`UseApiProblems`). An unreachable database answers `503` with `Retry-After`.

**Write ownership is split by domain.**

- **The Worker is the only writer of media data** (`MediaAsset`, `Metadata`, `ProxyFile`, `Burst`, `BurstCheck`, reconciliation, soft-deletes).
  - The API never touches `AnichronDbContext` outside DI wiring (`api-reaches-data-through-repositories`, `only-worker-writes-media`).
  - The one exception: `AssetInteraction`, written by the API through `IAssetInteractionWriter` (plain records in and out). It checks only `VisibleTo`, never `NotHiddenFor`, so a hidden asset can be unhidden. A first write that loses the `(UserId, AssetId)` race reloads and reapplies once.
- **The API owns identity, auth and admin writes**, through its services and `IUnitOfWork.SaveChangesAsync`. The exceptions: refresh rotation runs its own transaction (`EfRefreshTokenRepository.RotateAsync`, ADR 0017), and the Worker's `TokenCleanup` handler deletes expired refresh tokens.
- **Work items** are inserted only through `IWorkQueue`. The API's only other write to them is an admin's cancel (`IWorkItemCanceller`). Handlers live only in the Worker (`work-queue.md`). Raw SQL naming the table is beyond the architecture rules and is for review.
- **Proxy orphans** are written only by the Worker (`only-worker-writes-proxy-orphans`).
- Both hosts migrate at startup under a PostgreSQL advisory lock.

### Storage Layout

```
/data/originals/<user>/   NAS, mounted :ro at the SAME path in the API and that user's worker
/data/proxies/            local SSD; Worker writes, API reads (fixed StoragePath.ProxiesRoot)
```

- Proxy paths derive from content: `/data/proxies/{hash[0:2]}/{storage_config_id:N}-{hash}/{type}`. A retry overwrites its own debris; the config id keeps two users' identical files apart. `IProxyDirectoryStrategy` owns the rule.
- `ProxyFile.ProxyPath` is relative to `/data/proxies/`; `MediaAsset.FilePath` is relative to the storage config root.
- The API reads files only through `IProxyStore` and `IOriginalStore`, never `IFileSystem` (ADR 0007). A new kind of read is a new store method.

### Feature references (`docs/architecture/`)

Read the file before changing its area, and fix what your change makes wrong in it.

- `data-model.md`: entities, indexes, query filters, converters, NodaTime. `docs/schema.sql` is the authority on columns.
- `ingestion.md`: crawl, classification, reconciliation, Live Photos, proxies, bursts, duplicates, failure ownership.
- `work-queue.md`: lifecycle, adding a kind, the handler contract, proxy sweeps.
- `ui-serving.md`: serving rules, security headers, E2E setup.
- `media-reads.md`: `IAssetViewReader`, selections, proxy URLs, interaction rules.
- `ui-design-system.md`: stack, tokens, themes, components, accessibility checks.
- `ui-api.md`: the session, where data calls live, the signed-in user, the failure shape.

### Docs

`scripts/docs-guard.sh` (pre-commit and CI's `Docs` job) fails on a paragraph, list item or table cell over its word limit in CLAUDE.md, `docs/` or `.claude/docs/`.

- Docs are outlines: what holds, and where it lives in the code.
- No history, issue narratives, dates or measurements. Those go in the commit and the PR.
- Never copy a value, list or rule that lives in code, config or another doc; point to it.
- When code changes, correct the line that became wrong. Don't add a paragraph about the change.
- An ADR is Decision / Why / Rejected / Consequences, and states only what still holds.

## Conventions

- **Enforced by the build** (`TreatWarningsAsErrors`, `src/.editorconfig`, central package management, `dotnet format`): file-scoped namespaces matching the folder, `camelCase` private fields without underscore in production code, `I`-prefixed interfaces, 4 spaces, LF, `AnalysisMode=Recommended`, package versions only in `Directory.Packages.props`. Fix the code, don't suppress the rule.
- **Lock files:** CI restores in locked mode. After a package bump, run `dotnet restore src` and commit every changed `packages.lock.json`; Dependabot misses the test projects that reach a package through a project reference.
- **XML doc comments** only when they add information beyond the name. A summary needs a `<param>` per parameter (RCS1141); short ones are fine.

## Multi-User Design

- Each user has private `UserStorageConfig`s pointing at NAS roots. `RootPath` is globally unique and a **container** path, the same in every container (`/data/originals/<user>`).
- Workers: one per user (`WORKER__USER` + the required `WORKER__ROOTPATH`), or one for all users (configs added through the admin API).
- **Media is private per user.** Every media read goes through `MediaAssetVisibility.VisibleTo(userId)`: owner only, active config only, never soft-deleted, even under `IgnoreQueryFilters()`. Related data needs its own filter.
- "Not visible" is **`404`, never `403`**, so asset ids cannot be probed.
- Admins get no media access.
- `AssetInteraction` state is per user, never shared.

## Infrastructure

- **Images:** the Worker is `runtime:10.0-noble`, multi-arch (amd64 + arm64); its native packages are in its Dockerfile (ADR 0009, 0016). The API is Alpine.
- **CI** (`.github/workflows/ci.yml`): images push only after `CI gate` passes, tagged `sha-<hash>`, `edge` on master, SemVer on releases.
  - A draft PR runs no CI or CodeQL; its gate fails as `Draft, no CI`, never as `CI gate`.
  - A master push whose tree passed `CI gate` on its PR only publishes images (`scripts/tested-on-pr.sh`). The full sweeps, reports and badges run weekly (`docs/mutation-testing.md`, "The weekly full run").
  - A failed master push or weekly run opens a "Full run failed" issue assigned to the maintainer (`scripts/full-run-report.sh`).
- **Branch protection** requires only `CI gate` and `CodeQL`. A new required job goes into the gate's `needs` (`scripts/ci-gate.sh`), never into branch protection.
- **Backend jobs skip** on a PR that changes only `src/Anichron.UI/` and Markdown outside `src/` (except ADR 0005), as `scripts/changed-areas.sh` decides. A job that should skip with the backend gets `needs: changes`, `if: needs.changes.outputs.backend == 'true'` and a `--backend <job>` in the gate's call; `scripts/ci-gate-guard.sh` checks they agree.
- **Merging a PR:** only through `scripts/land.sh` ("Landing work"). It merges once `CI gate` and `CodeQL` pass; no confirmation from the maintainer is needed.
- **Configuration** (env vars):
  - The PostgreSQL connection comes only from `POSTGRES_CONNECTION__*` or Docker secrets.
  - `CORS__ALLOWEDORIGINS`: empty for same-origin; when set, the refresh cookie becomes `SameSite=None`.
  - `X-Forwarded-For` is trusted only from loopback, `FORWARDEDHEADERS__KNOWNPROXIES` or `KNOWNNETWORKS`. Rate limits partition on the resulting IP, so a reverse proxy must be listed. Never trust the Docker bridge for a published port.
  - Rate limits: `RATELIMIT__SENSITIVEPERMITLIMIT`, `RATELIMIT__REFRESHPERMITLIMIT`.
- Registration requires an admin invite, except for the first user (bootstrap admin).
- **Observability:** `GET /api/v1/healthz` (not in the OpenAPI document). Logging is the console logger with `LoggerMessage` source generation (`logging-is-source-generated`; `BeginScope` is allowed). No JSON logs or OpenTelemetry.
- License: AGPL-3.0.

## GitHub References

- **Issues:** https://github.com/bitbiter-dev/anichron/issues
- **Wiki** (early ADRs that older issues link to): https://github.com/bitbiter-dev/anichron/wiki. New ADRs go in `docs/adr/`.
- **What to work on next:**
  1. An open "Full run failed" issue goes first, then an open "Architecture review due" issue (`.claude/docs/design-process.md`, "Architecture review").
  2. Otherwise, an open `ready-for-agent` issue with no open blocker, in the order the epic milestones list.
  - A `needs-design` issue is picked only to write or approve its note; a `needs-decision` issue waits for the maintainer.

## Code graph (who uses a symbol)

Ask the graph before grepping for a C# symbol's callers, references or implementations:

```bash
scripts/graph.sh affected <Symbol>   # everything that depends on it, tests included
scripts/graph.sh explain <Symbol>    # direct callers and callees
```

Grep stays the tool for text, config and anything that is not a symbol.

## Landing work

Drafts run no CI, and `scripts/land.sh` puts one PR at a time on this machine into CI. After implementing, don't pause for the maintainer:

1. Run the tests you touched, and commit.
2. Start `scripts/mutation-changed.sh` and `scripts/ui-mutation-changed.sh` in the background; each skips when its area is unchanged. They run the unit suite; no separate full-suite run.
3. Push and open a **draft** PR, so the work is visible. Then run `/mattpocock-skills:code-review` (both subagents) while mutation runs.
4. Fix the findings. Run mutation again only if the fixes changed code.
5. Run `scripts/land.sh` in the background; its header says what it does and what each exit code means. Relay its `land:` and `with-lock:` lines to the chat with the Monitor tool, re-armed until it exits. On a failure, fix the cause and run it again.
6. Once merged, remove the issue from its epic milestone's order (the milestone description), so the next agent reads the next issue first.

- Don't poll CI, cancel another PR's run, or delete the remote branch (GitHub does on merge).
- On a CI failure, read the failed job's summary and `gh run view <id> --log-failed`, not the whole log.

## Engineering process (objection, first caller, design note)

Agents write issues, designs and code; the maintainer sets requirements and decides. Before writing an issue, designing, implementing or reviewing, read `.claude/docs/design-process.md`.

- **Objection:** when the requirement, design or spec is wrong for the system, object on the issue, relabel it and stop.
- **First caller:** every building block ships with the code that first uses it.
- **Design note:** an issue that changes the system's shape gets one, approved before `ready-for-agent`.

## Agent skills

### Issue tracker

GitHub issues in `bitbiter-dev/anichron`, through the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

The five canonical roles plus `needs-design` and `needs-decision`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `GLOSSARY.md` (written lazily by `/domain-modeling`) plus `docs/adr/`. See `docs/agents/domain.md`.
