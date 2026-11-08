# The API reads files through narrow stores, not a read-only IFileSystem

Status: accepted. Supersedes the `ReadOnlyFileSystem` approach of #149.

## Decision

The API reads files only through `IProxyStore` and `IOriginalStore` (`Anichron.Infrastructure/Storage/`). Each exposes the reads the API performs and nothing else. No API type depends on `IFileSystem` (`api-reads-no-file-system`).

- `IProxyStore.OpenRead(proxyPath)` resolves the path with `StoragePath.ResolveProxy` (join and containment check, shared with the Worker) and returns `null` for a missing file. `RootExists()` serves the health check.
- `IOriginalStore` has only `RootExists()` until serving originals adds its first read.
- Endpoint tests use a fake store; store tests use `MockFileSystem`.

## Why

The read-only `IFileSystem` decorator it replaces:

- threw on ordinary reads it did not forward, and only in production, since tests got a `MockFileSystem`;
- added nothing in production, where the `:ro` mounts already reject writes;
- left path joining and containment to each caller, which one forgot.

## Rejected

- **Forward every read in the decorator:** a 500-line surface that tracks every library release, and callers still redo path handling.
- **Inject `IFileSystem` and rely on `:ro` mounts:** nothing stops a write in code or tests.

## Consequences

A new kind of read is a new store method with its own tests. A general-purpose store method would bring the problem back.
