# UI API module

How the UI (`src/Anichron.UI`) calls the API.

## Where calls live

- `src/api/client.ts`: `createApiClient(tabs)` composes the router context's `api`, the session plus one module per area. Only `src/api/` touches the network (ESLint).
- `src/api/session.ts`: every call that issues, renews or ends a session (`restore`, `login`, `logout`), the timer, visibility and tab handling, and the signed-in state.
- `src/api/<area>.ts`: data calls, one module per area, created by its first caller. A data call is never a session method.

## The signed-in user

- The session holds the `user` from every session response and clears it with the session.
- `session.user()` is synchronous and stable between changes. Views read it through `useUser()` (`src/shared/use-user.ts`).
- Signed in means a user is held: route guards call `isSignedIn()` in `beforeLoad`.

## Failures

- A call the server refuses or cannot answer throws `ProblemError`, carrying the UI's `Problem` (`src/api/problem.ts`).
- `problemOf(error)` reads it, and anything else becomes the generic problem. A screen keys its messages by `ErrorCode`.

## The refresh rules

The single-flight refresh, its schedule and backoff, the `401` handling and the tab messages are in `session.ts` and written once. CLAUDE.md, "Session" and "Tabs", states them.
