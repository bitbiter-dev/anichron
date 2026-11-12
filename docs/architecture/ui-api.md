# UI API module

How the UI (`src/Anichron.UI`) calls the API.

## Where calls live

- `src/api/client.ts`: `createApiClient(tabs)` composes the router context's `api`, the session plus one module per area. Only `src/api/` touches the network (ESLint).
- `src/api/session.ts`: every call that issues, renews or ends a session (`restore`, `login`, `logout`), the timer, visibility and tab handling, and the signed-in state.
- `src/api/<area>.ts`: data calls, one module per area, created by its first caller. A data call is never a session method.

## Data calls

- An area module sends each call through `authorized`, which `createSession` hands to `client.ts`: the bearer token, and on a `401` one refresh and one retry. A token renewed meanwhile is retried without refreshing; a second `401` signs out.
- A refresh that fails without signing out throws `SessionNotRenewed`, and the query retry predicate (`src/app/query-client.ts`) stops on it.
- Proxy URLs in a response are relative to `/api/v1/`; the area module resolves them (`flashbacks.ts`).
- An `<img>` cannot see a `401`: its error calls `session.renewMedia()` once, then loads the source once more (`features/on-this-day/media.ts`). See "The refresh rules".

## The event stream

- `src/api/event-stream.ts` reads a Server-Sent Events response with `fetch`, so the request carries the bearer token through `authorized`. An area module imports it lazily, keeping the parser out of the initial bundle (`work-items.ts`).
- It reconnects from the last event id after a clean end, a network failure or a refusal, waiting `Retry-After` or its backoff. A `403` or `404` ends it for good.
- The caller owns the `AbortSignal`: the work queue closes its stream on unmount and while the tab is hidden (`features/work-items/use-stream.ts`).

## The signed-in user

- The session holds the `user` from every session response and clears it with the session.
- `session.user()` is synchronous and stable between changes. Views read it through `useUser()` (`src/shared/use-user.ts`).
- Signed in means a user is held: route guards call `isSignedIn()` in `beforeLoad`.

## Failures

- A call the server refuses or cannot answer throws `ProblemError`, carrying the UI's `Problem` (`src/api/problem.ts`).
- `problemOf(error)` reads it, and anything else becomes the generic problem. A screen keys its messages by `ErrorCode`.

## The refresh rules

The single-flight refresh, its schedule and backoff, the `401` handling and the tab messages are in `session.ts` and written once. CLAUDE.md, "Session" and "Tabs", states them.

- A media error refreshes only when the media cookie may have lapsed (`renewMedia()`). A lapsed cookie is the only `401` a proxy gives a signed-in user (ADR 0013), and refresh is rate-limited.
- A device clock fast enough to lose the cookie before `renewAt` keeps its media broken until the scheduled refresh.
