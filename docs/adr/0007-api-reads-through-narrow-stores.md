# The API reads files through narrow stores, not a read-only IFileSystem

Status: accepted. Supersedes the `ReadOnlyFileSystem` approach of #149.

The API reads files only through `IProxyStore` and `IOriginalStore` (`Anichron.Infrastructure/Storage/`).
Each exposes the reads the API actually performs and nothing else, so it has no write method to guard.
No API type depends on `IFileSystem` (architecture rule `api-reads-no-file-system`).

## Why the decorator went

#149 asked that a write by the API fail in tests instead of succeeding silently on `MockFileSystem`. It was
answered with `ReadOnlyFileSystem`: a 500-line decorator over the whole of `System.IO.Abstractions.IFileSystem`
whose writes threw `NotSupportedException`. The 2026-10-16 architecture review (#251) found:

- **It was not substitutable.** About half of its members, including ordinary reads such as `File.Exists`
  and `File.OpenRead`, threw `NotImplementedException` until someone added a forwarding override. Anything
  typed as `IFileSystem` could throw on a valid read.
- **It failed only in production.** API tests still received a `MockFileSystem`, so an unforwarded read
  passed every test. The `:ro` mounts already reject writes in production, so the decorator added no
  protection there either.
- **Its DI registration was hidden.** `IFileSystem` was registered inside `AddApiHealthChecks()`, which the
  proxy endpoint and `BlurhashReader` silently depended on.
- **Storage rules sat in the endpoint.** The proxy endpoint joined the proxy root with the stored path,
  checked containment and mapped "missing" to 404. `BlurhashReader` repeated the join without the
  containment check (#267), and serving originals (#44) would have repeated it again.

## Decision

- `IProxyStore.OpenRead(proxyPath)` owns the join with `StoragePath.ProxiesRoot`, the containment check
  (`StoragePath.IsInside`, after `GetFullPath`) and "missing file or directory means `null`". Any other I/O
  failure propagates. `RootExists()` serves the health check.
- `IOriginalStore` has only `RootExists()`, for the originals health check. Reading an original is added
  with #44, its first caller, not ahead of it.
- The implementations live in Infrastructure because the API may not touch `IFileSystem`. A public
  constructor builds a real `FileSystem`; an internal one takes `IFileSystem` for `MockFileSystem` tests.
  Both stores are registered with the API's other services, not with the health checks.
- Endpoint tests use a fake store; store tests use `MockFileSystem`.

## Considered options

- **Keep the decorator and forward every read.** Rejected: it stays a 500-line surface that must track
  every `System.IO.Abstractions` release, and it still lets each caller redo path handling.
- **Inject `IFileSystem` directly and rely on the `:ro` mounts.** Rejected: nothing stops a write in code
  or tests, and path containment stays the caller's job.

## Consequences

- A new kind of read is a new store method with its own tests. Adding a general-purpose method to a store
  re-creates the problem this ADR removes.
- The BlurHash moved out of the file system at the same time (#252), so the stores serve only proxies and,
  later, originals.
